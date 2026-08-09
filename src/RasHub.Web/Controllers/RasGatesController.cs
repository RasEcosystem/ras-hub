using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Tasks;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.BackgroundTasks.Models;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Contracts.RasHub.Responses;
using RasHub.Domain;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
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
    [HttpGet("{id:guid}/status")]
    [EndpointSummary("Get status")]
    [EndpointDescription(
        "Returns the cached status without contacting the gateway.")]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateStatusResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<RasGateStatusResponse>> GetStatus(
        Guid id,
        [FromServices] RasGateQueries query,
        CancellationToken cancellationToken)
    {
        var rasGate = await query.GetByIdAsync(id, cancellationToken);

        if (rasGate is null)
            return CreateStatusNotFoundResponse(id);

        if (!rasGate.IsActive)
            return CreateInactiveStatusResponse(id);

        var status = await query.GetStatusAsync(id, cancellationToken);

        return ApiResponse<RasGateStatusResponse>.Ok(status);
    }

    [HttpPost("{id:guid}/status/check")]
    [EndpointSummary("Check status")]
    [EndpointDescription(
        "Checks the current gateway status, persists it, and returns the observed status.")]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateStatusResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status502BadGateway)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<RasGateStatusResponse>> CheckStatus(
        Guid id,
        [FromServices] RasGateQueries query,
        [FromServices] IBackgroundTaskEngine backgroundTaskEngine,
        CancellationToken cancellationToken)
    {
        var rasGate = await query.GetByIdAsync(id, cancellationToken);

        if (rasGate is null)
            return CreateStatusNotFoundResponse(id);

        if (!rasGate.IsActive)
            return CreateInactiveStatusResponse(id);

        BackgroundTaskHandle handle;

        try
        {
            handle = backgroundTaskEngine.Enqueue(
                new CheckRasGateStatusTask(id),
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
                "ras_gate_status_check_unavailable",
                "RasGate status check could not be scheduled.");
        }

        var result = await handle.WaitAsync(cancellationToken);

        if (!result.IsSucceeded)
            return CreateStatusCheckFailureResponse(result);

        var status = await query.GetStatusAsync(id, cancellationToken);

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
            return CreateInvalidEndpointResponse();
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
            return CreateNotFoundResponse(id);

        var endpointChanged =
            !string.Equals(rasGate.Url, request.Url, StringComparison.Ordinal) ||
            rasGate.Port != request.Port;

        if (endpointChanged && request.ApiKey is null)
            return CreateApiKeyRequiredResponse();

        try
        {
            _ = endpointFactory.CreateBaseAddress(request.Url, request.Port);
        }
        catch (RasGateEndpointValidationException)
        {
            return CreateInvalidEndpointResponse();
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
            return CreateConcurrentUpdateResponse();
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
            return CreateNotFoundResponse(id);

        repository.Remove(rasGate);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CreateConcurrentUpdateResponse();
        }

        return ApiResponse<RasGateModel>.Ok(ToModel(rasGate));
    }

    private static ApiResponse<RasGateModel> CreateNotFoundResponse(Guid id)
    {
        return ApiResponse<RasGateModel>.Fail(
            HttpStatusCode.NotFound,
            "ras_gate_not_found",
            $"RasGate '{id}' was not found.");
    }

    private static ApiResponse<RasGateModel> CreateInvalidEndpointResponse()
    {
        return ApiResponse<RasGateModel>.Fail(
            HttpStatusCode.BadRequest,
            "ras_gate_endpoint_invalid",
            "The RasGate endpoint is invalid.");
    }

    private static ApiResponse<RasGateModel> CreateApiKeyRequiredResponse()
    {
        return ApiResponse<RasGateModel>.Fail(
            HttpStatusCode.BadRequest,
            "ras_gate_api_key_required",
            "A new RasGate API key is required when the endpoint changes.");
    }

    private static ApiResponse<RasGateModel> CreateConcurrentUpdateResponse()
    {
        return ApiResponse<RasGateModel>.Fail(
            HttpStatusCode.Conflict,
            "ras_gate_concurrency_conflict",
            "RasGate configuration changed concurrently. Retry with current data.");
    }

    private static ApiResponse<RasGateStatusResponse> CreateStatusNotFoundResponse(
        Guid id)
    {
        return ApiResponse<RasGateStatusResponse>.Fail(
            HttpStatusCode.NotFound,
            "ras_gate_not_found",
            $"RasGate '{id}' was not found.");
    }

    private static ApiResponse<RasGateStatusResponse> CreateInactiveStatusResponse(
        Guid id)
    {
        return ApiResponse<RasGateStatusResponse>.Fail(
            HttpStatusCode.Conflict,
            "ras_gate_inactive",
            $"RasGate '{id}' is inactive.");
    }

    private static ApiResponse<RasGateStatusResponse> CreateStatusCheckFailureResponse(
        BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<RasGateStatusResponse>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_status_check_canceled",
                "RasGate status check was canceled.");

        if (result.Exception is RasGateInactiveException inactiveException)
            return CreateInactiveStatusResponse(inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<RasGateStatusResponse>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during status check.");

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
            rasGate.IsActive,
            rasGate.CreatedAt,
            rasGate.UpdatedAt);
    }
}
