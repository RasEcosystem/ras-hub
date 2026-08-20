using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Application.RasGates.Tasks.Status;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Responses;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Authentication;
using RasHub.Web.Infrastructure.RasGates;

namespace RasHub.Web.Controllers.RasGates;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-gates/{rasGateId:guid}/status")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[ControllerDescription(
    "Inspect and synchronize a registered gateway status.")]
public sealed class RasGateStatusController(
    ActiveRasGateLookup rasGateLookup,
    RasGateQueries queries,
    InteractiveTaskRunner taskRunner) : ControllerBase
{
    [HttpGet]
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
    public async Task<ApiResponse<RasGateStatusResponse>> Get(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses.ForUnavailableGate<RasGateStatusResponse>(
                state,
                rasGateId);

        var status = await queries.GetStatusAsync(
            rasGateId,
            cancellationToken);

        return ApiResponse<RasGateStatusResponse>.Ok(status);
    }

    [HttpPost("synchronize")]
    [EndpointSummary("Synchronize status")]
    [EndpointDescription(
        "Synchronizes the current gateway status, persists it, and returns the observed status.")]
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
    public async Task<ApiResponse<RasGateStatusResponse>> Synchronize(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses.ForUnavailableGate<RasGateStatusResponse>(
                state,
                rasGateId);

        var execution = await taskRunner.RunAsync(
            new CheckRasGateStatusTask(rasGateId),
            RasGateTaskOptions.InteractiveStatusSynchronization(rasGateId),
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses.StatusSynchronizationRejected();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses.StatusSynchronizationFailed(
                taskResult);

        var status = await queries.GetStatusAsync(
            rasGateId,
            cancellationToken);

        return status is null
            ? RasGateApiResponses.GateNotFound<RasGateStatusResponse>(rasGateId)
            : ApiResponse<RasGateStatusResponse>.Ok(status);
    }
}