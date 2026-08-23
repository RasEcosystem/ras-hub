using System.Net;
using RasHub.Application.RasGates.Exceptions;
using RasHub.BackgroundTasks.Models;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Responses;

namespace RasHub.Web.Api.RasGates;

internal static partial class RasGateApiResponses
{
    public static ApiResponse<RasGateStatusResponse>
        StatusLiveRefreshRejected()
    {
        return ApiResponse<RasGateStatusResponse>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_status_live_refresh_unavailable",
            "The live RasGate status refresh could not be scheduled.");
    }

    public static ApiResponse<RasGateStatusResponse>
        StatusLiveRefreshFailed(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<RasGateStatusResponse>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_status_live_refresh_canceled",
                "The live RasGate status refresh was canceled.");

        if (TryMapLocalStateFailure<RasGateStatusResponse>(result) is
            { } localStateFailure)
            return localStateFailure;

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<RasGateStatusResponse>(
                inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<RasGateStatusResponse>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during the live status refresh.");

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
}
