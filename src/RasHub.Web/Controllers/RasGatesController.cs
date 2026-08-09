using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Domain;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Authentication;
using RasHub.Web.Infrastructure.Authorization;

namespace RasHub.Web.Controllers;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-gates")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[ControllerDescription(
    "Manage registered gateways.")]
public sealed class RasGatesController : ControllerBase
{
    [HttpPost("get-paged")]
    [EndpointSummary("List gateways")]
    [EndpointDescription(
        "Returns a paginated collection of registered gateways.")]
    [ProducesResponseType(
        typeof(ApiResponse<PageResult<RasGateModel>>),
        StatusCodes.Status200OK)]
    public async Task<ApiResponse<PageResult<RasGateModel>>> GetPaged(
        [FromBody] PageRequest request,
        [FromServices] RasGateQueries query,
        CancellationToken cancellationToken)
    {
        var result = await query.GetPagedAsync(
            request,
            cancellationToken);

        return ApiResponse<PageResult<RasGateModel>>.Ok(result);
    }

    [HttpGet("{id:guid}")]
    [EndpointSummary("Get gateway")]
    [EndpointDescription(
        "Returns the gateway connection details. The stored API key is never returned.")]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateModel>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    public async Task<ApiResponse<RasGateModel>> GetById(
        Guid id,
        [FromServices] RasGateQueries query,
        CancellationToken cancellationToken)
    {
        var rasGate = await query.GetByIdAsync(
            id,
            cancellationToken);

        if (rasGate is null)
            return RasGateApiResponses.GateNotFound<RasGateModel>(id);

        return ApiResponse<RasGateModel>.Ok(rasGate);
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.ManageRasGates)]
    [EndpointSummary("Register gateway")]
    [EndpointDescription(
        "Registers a gateway connection and returns its public details.")]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateModel>),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status400BadRequest)]
    public async Task<ApiResponse<RasGateModel>> Create(
        [FromBody] CreateRasGateRequest request,
        [FromServices] IRepository<RasGate> repository,
        [FromServices] IRasGateEndpointFactory endpointFactory,
        [FromServices] IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = endpointFactory.CreateBaseAddress(request.Url, request.Port);
        }
        catch (RasGateEndpointValidationException)
        {
            return RasGateApiResponses.InvalidEndpoint();
        }

        var rasGate = new RasGate
        {
            Name = request.Name,
            Url = request.Url,
            Port = request.Port,
            ApiKey = request.ApiKey,
            IsActive = request.IsActive
        };

        await repository.AddAsync(rasGate, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var model = ToModel(rasGate);
        var location = Url.ActionLink(nameof(GetById), values: new { id = rasGate.Id });

        if (location is not null)
            Response.Headers.Location = location;

        return ApiResponse<RasGateModel>.Created(model);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.ManageRasGates)]
    [EndpointSummary("Update gateway")]
    [EndpointDescription(
        "Updates the gateway connection and activity. Endpoint changes require a new API key; otherwise an omitted API key or activity value is preserved.")]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateModel>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    public async Task<ApiResponse<RasGateModel>> Update(
        Guid id,
        [FromBody] UpdateRasGateRequest request,
        [FromServices] IRepository<RasGate> repository,
        [FromServices] IRasClusterSnapshotStore snapshotStore,
        [FromServices] IRasGateEndpointFactory endpointFactory,
        [FromServices] IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var rasGate = await repository.GetByIdAsync(id, cancellationToken);

        if (rasGate is null)
            return RasGateApiResponses.GateNotFound<RasGateModel>(id);

        var endpointChanged =
            !string.Equals(rasGate.Url, request.Url, StringComparison.Ordinal) ||
            rasGate.Port != request.Port;

        if (endpointChanged && request.ApiKey is null)
            return RasGateApiResponses.ApiKeyRequired();

        try
        {
            _ = endpointFactory.CreateBaseAddress(request.Url, request.Port);
        }
        catch (RasGateEndpointValidationException)
        {
            return RasGateApiResponses.InvalidEndpoint();
        }

        var apiKeyChanged = request.ApiKey is not null &&
                            !string.Equals(
                                rasGate.ApiKey,
                                request.ApiKey,
                                StringComparison.Ordinal);
        var remoteIdentityChanged = endpointChanged || apiKeyChanged;
        var deactivated = request.IsActive == false && rasGate.IsActive;

        rasGate.Name = request.Name;
        rasGate.Url = request.Url;
        rasGate.Port = request.Port;

        if (request.ApiKey is not null)
            rasGate.ApiKey = request.ApiKey;

        if (request.IsActive is { } isActive && rasGate.IsActive != isActive)
        {
            rasGate.IsActive = isActive;

            if (isActive)
                rasGate.LastSeenAt = null;
        }

        if (remoteIdentityChanged || deactivated)
        {
            rasGate.InstanceName = null;
            rasGate.Version = null;
            rasGate.StatusObservedAt = null;
            rasGate.LastSeenAt = null;
            await snapshotStore.InvalidateAsync(id, cancellationToken);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RasGateApiResponses.ConcurrentUpdate();
        }

        return ApiResponse<RasGateModel>.Ok(ToModel(rasGate));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPolicies.ManageRasGates)]
    [EndpointSummary("Delete gateway")]
    [EndpointDescription(
        "Removes the gateway from regular queries while retaining its stored record.")]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateModel>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    public async Task<ApiResponse<RasGateModel>> Delete(
        Guid id,
        [FromServices] IRepository<RasGate> repository,
        [FromServices] IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var rasGate = await repository.GetByIdAsync(id, cancellationToken);

        if (rasGate is null)
            return RasGateApiResponses.GateNotFound<RasGateModel>(id);

        repository.Remove(rasGate);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RasGateApiResponses.ConcurrentUpdate();
        }

        return ApiResponse<RasGateModel>.Ok(ToModel(rasGate));
    }

    private static RasGateModel ToModel(RasGate rasGate)
    {
        return new RasGateModel(
            rasGate.Id,
            rasGate.Name,
            rasGate.Url,
            rasGate.Port,
            rasGate.IsActive,
            rasGate.CreatedAt,
            rasGate.UpdatedAt);
    }
}