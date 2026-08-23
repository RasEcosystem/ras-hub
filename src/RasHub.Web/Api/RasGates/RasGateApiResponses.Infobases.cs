using System.Net;
using RasHub.Application.RasGates.Exceptions;
using RasHub.BackgroundTasks.Models;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Responses;

namespace RasHub.Web.Api.RasGates;

internal static partial class RasGateApiResponses
{
    public static ApiResponse<InfobaseModel>
        InfobaseLiveRefreshRejected()
    {
        return InfobaseLiveRefreshRejected<InfobaseModel>();
    }

    public static ApiResponse<T> InfobaseLiveRefreshRejected<T>()
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "infobase_live_refresh_unavailable",
            "The live infobase refresh could not be scheduled.");
    }

    public static ApiResponse<InfobaseModel>
        InfobaseLiveRefreshFailed(BackgroundTaskResult result)
    {
        return InfobaseLiveRefreshFailed<InfobaseModel>(result);
    }

    public static ApiResponse<T> InfobaseLiveRefreshFailed<T>(
        BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<T>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "infobase_live_refresh_canceled",
                "The live infobase refresh was canceled.");

        if (TryMapLocalStateFailure<T>(result) is { } localStateFailure)
            return localStateFailure;

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<T>(inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<T>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during the live infobase refresh.");

        if (result.Exception is RacResourceNotFoundException
            {
                Resource: "infobases"
            } notFoundException)
            return InfobaseNotFound<T>(notFoundException.ExternalId);

        if (TryMapRacFailure<T>(result) is { } racFailure)
            return racFailure;

        if (result.Exception is TimeoutException)
            return ApiResponse<T>.Fail(
                HttpStatusCode.GatewayTimeout,
                "infobase_live_refresh_timeout",
                "The live infobase refresh through RasGate timed out.");

        return ApiResponse<T>.Fail(
            HttpStatusCode.BadGateway,
            "infobase_live_refresh_failed",
            "The live infobase could not be retrieved and published to the shadow.");
    }

    public static ApiResponse<T> InfobaseShadowRefreshRejected<T>()
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "infobase_shadow_refresh_unavailable",
            "The infobase shadow refresh could not be scheduled.");
    }

    public static ApiResponse<T> InfobaseShadowRefreshFailed<T>(
        BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<T>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "infobase_shadow_refresh_canceled",
                "The infobase shadow refresh was canceled.");

        if (TryMapLocalStateFailure<T>(result) is { } localStateFailure)
            return localStateFailure;

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<T>(inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<T>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during the infobase shadow refresh.");

        if (TryMapRacFailure<T>(result) is { } racFailure)
            return racFailure;

        if (result.Exception is TimeoutException)
            return ApiResponse<T>.Fail(
                HttpStatusCode.GatewayTimeout,
                "infobase_shadow_refresh_timeout",
                "The infobase shadow refresh through RasGate timed out.");

        return ApiResponse<T>.Fail(
            HttpStatusCode.BadGateway,
            "infobase_shadow_refresh_failed",
            "The live infobase snapshot could not be published to the shadow.");
    }
}
