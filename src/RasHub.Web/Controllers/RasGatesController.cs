using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Tasks;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Contracts.RasHub.Responses;
using RasHub.Domain;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Exceptions;
using RasHub.Synchronization.Models;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Authentication;

namespace RasHub.Web.Controllers;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-gates")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[ControllerDescription(
    "Manage registered gateways.")]
public sealed class RasGatesController : ControllerBase
{
    [HttpGet("{id:guid}/status")]
    [EndpointSummary("Get status")]
    [EndpointDescription(
        "Returns the cached status. Set refresh=true to request a fresh status first.")]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateStatusResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status502BadGateway)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<RasGateStatusResponse>> GetStatus(
        Guid id,
        [FromServices] RasGateQueries query,
        [FromServices] ISynchronizationEngine synchronizationEngine,
        CancellationToken cancellationToken,
        [FromQuery] bool refresh = false)
    {
        var status = await query.GetStatusAsync(id, cancellationToken);

        if (status is null)
            return CreateStatusNotFoundResponse(id);

        if (!refresh)
            return ApiResponse<RasGateStatusResponse>.Ok(status);

        BackgroundTaskHandle handle;

        try
        {
            handle = synchronizationEngine.Enqueue(
                new RefreshRasGateStatusTask(id),
                new BackgroundTaskOptions
                {
                    Queue = BackgroundTaskQueue.Interactive,
                    MaxAttempts = 2,
                    RetryDelay = TimeSpan.FromMilliseconds(250),
                    Timeout = TimeSpan.FromSeconds(10),
                    DeduplicationKey = $"ras-gate-status:{id}",
                    ConcurrencyKey = $"ras-gate:{id}"
                });
        }
        catch (BackgroundTaskRejectedException)
        {
            return ApiResponse<RasGateStatusResponse>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_refresh_unavailable",
                "RasGate status refresh could not be scheduled.");
        }

        var result = await handle.WaitAsync(cancellationToken);

        if (!result.IsSucceeded)
            return CreateRefreshFailureResponse(result);

        status = await query.GetStatusAsync(id, cancellationToken);

        return status is null
            ? CreateStatusNotFoundResponse(id)
            : ApiResponse<RasGateStatusResponse>.Ok(status);
    }

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
            return CreateNotFoundResponse(id);

        return ApiResponse<RasGateModel>.Ok(rasGate);
    }

    [HttpPost]
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
        [FromServices] IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var rasGate = new RasGate
        {
            Name = request.Name,
            Url = request.Url,
            Port = request.Port,
            ApiKey = request.ApiKey
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
    [EndpointSummary("Update gateway")]
    [EndpointDescription(
        "Updates the gateway connection. When the API key is omitted, the stored key is preserved.")]
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
        [FromServices] IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var rasGate = await repository.GetByIdAsync(id, cancellationToken);

        if (rasGate is null)
            return CreateNotFoundResponse(id);

        rasGate.Name = request.Name;
        rasGate.Url = request.Url;
        rasGate.Port = request.Port;

        if (request.ApiKey is not null)
            rasGate.ApiKey = request.ApiKey;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ApiResponse<RasGateModel>.Ok(ToModel(rasGate));
    }

    [HttpDelete("{id:guid}")]
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
            return CreateNotFoundResponse(id);

        repository.Remove(rasGate);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ApiResponse<RasGateModel>.Ok(ToModel(rasGate));
    }

    private static ApiResponse<RasGateModel> CreateNotFoundResponse(Guid id)
    {
        return ApiResponse<RasGateModel>.Fail(
            HttpStatusCode.NotFound,
            "ras_gate_not_found",
            $"RasGate '{id}' was not found.");
    }

    private static ApiResponse<RasGateStatusResponse> CreateStatusNotFoundResponse(
        Guid id)
    {
        return ApiResponse<RasGateStatusResponse>.Fail(
            HttpStatusCode.NotFound,
            "ras_gate_not_found",
            $"RasGate '{id}' was not found.");
    }

    private static ApiResponse<RasGateStatusResponse> CreateRefreshFailureResponse(
        BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<RasGateStatusResponse>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_refresh_canceled",
                "RasGate status refresh was canceled.");

        if (result.Exception is TimeoutException)
            return ApiResponse<RasGateStatusResponse>.Fail(
                HttpStatusCode.GatewayTimeout,
                "ras_gate_timeout",
                "RasGate did not respond in time.");

        return ApiResponse<RasGateStatusResponse>.Fail(
            HttpStatusCode.BadGateway,
            "ras_gate_unavailable",
            "RasGate status could not be retrieved.");
    }

    private static RasGateModel ToModel(RasGate rasGate)
    {
        return new RasGateModel(
            rasGate.Id,
            rasGate.Name,
            rasGate.Url,
            rasGate.Port,
            rasGate.CreatedAt,
            rasGate.UpdatedAt);
    }
}