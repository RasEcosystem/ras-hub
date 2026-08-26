using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Services;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Domain;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Authentication;
using RasHub.Web.Infrastructure.Authorization;

namespace RasHub.Web.Controllers.RasGates;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-gates")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[Tags("RasGates")]
[ControllerDescription("RasGates",
    "Inspect and manage Hub-owned RasGate registrations and their observed status.")]
public sealed class RasGateAdministrationController(
    RasGateRegistry rasGateRegistry) : ControllerBase
{
    [HttpPost(Name = "RegisterRasGate")]
    [Authorize(Policy = AppPolicies.ManageRasGates)]
    [EndpointSummary("Register gateway")]
    [EndpointDescription(
        "Registers a RasGate connection in the Hub and returns its public details.")]
    [ProducesResponseType<ApiResponse<RasGateModel>>(StatusCodes.Status201Created)]
    [ProducesApiErrors(StatusCodes.Status400BadRequest)]
    public async Task<ApiResponse<RasGateModel>> Register(
        [FromBody] CreateRasGateRequest request,
        CancellationToken cancellationToken)
    {
        RasGate rasGate;
        try
        {
            rasGate = await rasGateRegistry.RegisterAsync(
                new RasGateRegistration(
                    request.Name,
                    request.Url,
                    request.Port,
                    request.ApiKey,
                    request.IsActive),
                cancellationToken);
        }
        catch (RasGateEndpointValidationException)
        {
            return RasGateApiResponses.InvalidEndpoint();
        }

        var model = ToModel(rasGate);
        var location = Url.Link(
            "GetRasGate",
            new { rasGateId = rasGate.Id });

        if (location is not null)
            Response.Headers.Location = location;

        return ApiResponse<RasGateModel>.Created(model);
    }

    [HttpPut("{rasGateId:guid}", Name = "UpdateRasGate")]
    [Authorize(Policy = AppPolicies.ManageRasGates)]
    [EndpointSummary("Update gateway")]
    [EndpointDescription(
        "Replaces the registered connection and activity state. Endpoint changes require a new API key; otherwise an omitted API key is preserved.")]
    [ProducesResponseType<ApiResponse<RasGateModel>>(StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<RasGateModel>> Update(
        Guid rasGateId,
        [FromBody] UpdateRasGateRequest request,
        CancellationToken cancellationToken)
    {
        RasGate? rasGate;
        try
        {
            rasGate = await rasGateRegistry.UpdateAsync(
                rasGateId,
                new RasGateRegistrationUpdate(
                    request.Name,
                    request.Url,
                    request.Port,
                    request.IsActive,
                    request.ApiKey),
                cancellationToken);
        }
        catch (RasGateApiKeyRequiredException)
        {
            return RasGateApiResponses.ApiKeyRequired();
        }
        catch (RasGateEndpointValidationException)
        {
            return RasGateApiResponses.InvalidEndpoint();
        }
        catch (DbUpdateConcurrencyException)
        {
            return RasGateApiResponses.ConcurrentUpdate();
        }

        if (rasGate is null)
            return RasGateApiResponses.GateNotFound<RasGateModel>(rasGateId);

        return ApiResponse<RasGateModel>.Ok(ToModel(rasGate));
    }

    [HttpDelete("{rasGateId:guid}", Name = "UnregisterRasGate")]
    [Authorize(Policy = AppPolicies.ManageRasGates)]
    [EndpointSummary("Unregister gateway")]
    [EndpointDescription(
        "Removes the RasGate registration from regular queries while retaining its stored record.")]
    [ProducesResponseType<ApiResponse<RasGateModel>>(StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<RasGateModel>> Unregister(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        RasGate? rasGate;
        try
        {
            rasGate = await rasGateRegistry.UnregisterAsync(
                rasGateId,
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RasGateApiResponses.ConcurrentUpdate();
        }

        if (rasGate is null)
            return RasGateApiResponses.GateNotFound<RasGateModel>(rasGateId);

        return ApiResponse<RasGateModel>.Ok(ToModel(rasGate));
    }

    private static RasGateModel ToModel(RasGate rasGate)
    {
        return new RasGateModel
        {
            Id = rasGate.Id,
            Name = rasGate.Name,
            Url = rasGate.Url,
            Port = rasGate.Port,
            IsActive = rasGate.IsActive,
            CreatedAt = rasGate.CreatedAt,
            UpdatedAt = rasGate.UpdatedAt
        };
    }
}
