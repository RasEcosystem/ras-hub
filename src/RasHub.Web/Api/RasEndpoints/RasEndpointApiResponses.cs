using System.Net;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Models;

namespace RasHub.Web.Api.RasEndpoints;

internal static class RasEndpointApiResponses
{
    public static ApiResponse<T> ForUnavailableEndpoint<T>(
        ActiveRasEndpointState state,
        Guid rasEndpointId)
    {
        return state switch
        {
            ActiveRasEndpointState.NotFound =>
                EndpointNotFound<T>(rasEndpointId),
            ActiveRasEndpointState.Inactive =>
                EndpointInactive<T>(rasEndpointId),
            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "The RAS endpoint is available.")
        };
    }

    public static ApiResponse<T> EndpointNotFound<T>(Guid rasEndpointId)
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.NotFound,
            "ras_endpoint_not_found",
            $"RAS endpoint '{rasEndpointId}' was not found.");
    }

    public static ApiResponse<RasEndpointModel> InvalidAddress()
    {
        return ApiResponse<RasEndpointModel>.Fail(
            HttpStatusCode.BadRequest,
            "ras_endpoint_address_invalid",
            "The RAS endpoint address is invalid.");
    }

    public static ApiResponse<RasEndpointModel> GateNotFound(Guid rasGateId)
    {
        return ApiResponse<RasEndpointModel>.Fail(
            HttpStatusCode.NotFound,
            "ras_gate_not_found",
            $"RasGate '{rasGateId}' was not found.");
    }

    public static ApiResponse<RasEndpointModel> ConcurrentUpdate()
    {
        return ApiResponse<RasEndpointModel>.Fail(
            HttpStatusCode.Conflict,
            "ras_endpoint_concurrency_conflict",
            "RAS endpoint configuration changed concurrently. Retry with current data.");
    }

    public static ApiResponse<T> EndpointInactive<T>(Guid rasEndpointId)
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.Conflict,
            "ras_endpoint_inactive",
            $"RAS endpoint '{rasEndpointId}' is inactive.");
    }

    public static ApiResponse<T> GateUnavailable<T>(
        Guid rasEndpointId,
        Guid rasGateId)
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_endpoint_gate_unavailable",
            $"RAS endpoint '{rasEndpointId}' is assigned to unavailable " +
            $"RasGate '{rasGateId}'.");
    }

    public static ApiResponse<T> ConfigurationChanged<T>(
        Guid rasEndpointId)
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.Conflict,
            "ras_endpoint_configuration_changed",
            $"RAS endpoint '{rasEndpointId}' or its execution Gate changed " +
            "while the operation was in progress.");
    }
}
