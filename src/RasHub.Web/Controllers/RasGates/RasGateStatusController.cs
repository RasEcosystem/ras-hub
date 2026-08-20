using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
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
[Tags("RasGates")]
[ControllerDescription("RasGates",
    "Manage registered gateways and inspect their synchronized status.")]
public sealed class RasGateStatusController(
    RasGateQueries queries,
    InteractiveTaskRunner taskRunner,
    TimeProvider timeProvider,
    IOptions<RasGateMonitoringOptions> monitoringOptions) : ControllerBase
{
    [HttpGet(Name = "GetRasGateStatus")]
    [EndpointSummary("Get status")]
    [EndpointDescription(
        "Returns the last observed RasGate and RAC status without contacting the gateway.")]
    [ProducesResponseType<ApiResponse<RasGateStatusResponse>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<RasGateStatusResponse>> Get(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        var result = await queries.GetStatusAsync(
            rasGateId,
            GetOnlineSince(),
            cancellationToken);

        return result is null
            ? RasGateApiResponses.GateNotFound<RasGateStatusResponse>(rasGateId)
            : result.IsActive
                ? ApiResponse<RasGateStatusResponse>.Ok(result.Status)
                : RasGateApiResponses
                    .ForUnavailableGate<RasGateStatusResponse>(
                        ActiveRasGateState.Inactive,
                        rasGateId);
    }

    [HttpPost("synchronize", Name = "SynchronizeRasGateStatus")]
    [EndpointSummary("Synchronize status")]
    [EndpointDescription(
        "Observes RasGate and RAC, persists their aggregate status, and returns it.")]
    [ProducesResponseType<ApiResponse<RasGateStatusResponse>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<RasGateStatusResponse>> Synchronize(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        var current = await queries.GetStatusAsync(
            rasGateId,
            GetOnlineSince(),
            cancellationToken);

        if (current is null)
            return RasGateApiResponses.GateNotFound<RasGateStatusResponse>(
                rasGateId);

        if (!current.IsActive)
            return RasGateApiResponses
                .ForUnavailableGate<RasGateStatusResponse>(
                    ActiveRasGateState.Inactive,
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

        var refreshed = await queries.GetStatusAsync(
            rasGateId,
            GetOnlineSince(),
            cancellationToken);

        return refreshed is null
            ? RasGateApiResponses.GateNotFound<RasGateStatusResponse>(rasGateId)
            : refreshed.IsActive
                ? ApiResponse<RasGateStatusResponse>.Ok(refreshed.Status)
                : RasGateApiResponses
                    .ForUnavailableGate<RasGateStatusResponse>(
                        ActiveRasGateState.Inactive,
                        rasGateId);
    }

    private DateTime GetOnlineSince()
    {
        return (timeProvider.GetUtcNow() - monitoringOptions.Value.OnlineThreshold)
            .UtcDateTime;
    }
}
