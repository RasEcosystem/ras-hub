using System.Net;
using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.BackgroundTasks.Models;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Models;
using RasHub.Web.Api.RasEndpoints;

namespace RasHub.Web.Api.RasGates;

internal static partial class RasGateApiResponses
{
    public static ApiResponse<T> ForUnavailableGate<T>(
        ActiveRasGateState state,
        Guid rasGateId)
    {
        return state switch
        {
            ActiveRasGateState.NotFound => GateNotFound<T>(rasGateId),
            ActiveRasGateState.Inactive => GateInactive<T>(rasGateId),
            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "The RasGate is available.")
        };
    }

    public static ApiResponse<T> GateNotFound<T>(Guid rasGateId)
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.NotFound,
            "ras_gate_not_found",
            $"RasGate '{rasGateId}' was not found.");
    }

    public static ApiResponse<RasGateModel> InvalidEndpoint()
    {
        return ApiResponse<RasGateModel>.Fail(
            HttpStatusCode.BadRequest,
            "ras_gate_endpoint_invalid",
            "The RasGate endpoint is invalid.");
    }

    public static ApiResponse<RasGateModel> ApiKeyRequired()
    {
        return ApiResponse<RasGateModel>.Fail(
            HttpStatusCode.BadRequest,
            "ras_gate_api_key_required",
            "A new RasGate API key is required when the endpoint changes.");
    }

    public static ApiResponse<RasGateModel> ConcurrentUpdate()
    {
        return ApiResponse<RasGateModel>.Fail(
            HttpStatusCode.Conflict,
            "ras_gate_concurrency_conflict",
            "RasGate configuration changed concurrently. Retry with current data.");
    }

    public static ApiResponse<ClusterModel> ClusterNotFound(Guid clusterId)
    {
        return ClusterNotFound<ClusterModel>(clusterId);
    }

    public static ApiResponse<T> ClusterNotFound<T>(Guid clusterId)
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.NotFound,
            "cluster_not_found",
            $"Cluster '{clusterId}' was not found.");
    }

    public static ApiResponse<InfobaseModel> InfobaseNotFound(
        Guid infobaseId)
    {
        return InfobaseNotFound<InfobaseModel>(infobaseId);
    }

    public static ApiResponse<T> InfobaseNotFound<T>(Guid infobaseId)
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.NotFound,
            "infobase_not_found",
            $"Infobase '{infobaseId}' was not found.");
    }

    private static ApiResponse<T>? TryMapRacFailure<T>(
        BackgroundTaskResult result)
    {
        if (result.Exception is RacUnavailableException)
            return ApiResponse<T>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "rac_unavailable",
                "RAC is unavailable through RasGate.");

        if (result.Exception is RasGateCapabilityNotSupportedException)
            return ApiResponse<T>.Fail(
                HttpStatusCode.Conflict,
                "rac_capability_not_supported",
                "The requested RAC operation is not supported.");

        return null;
    }

    private static ApiResponse<T>? TryMapLocalStateFailure<T>(
        BackgroundTaskResult result)
    {
        if (result.Exception is RasEndpointNotFoundException endpointNotFound)
            return RasEndpointApiResponses.EndpointNotFound<T>(
                endpointNotFound.RasEndpointId);

        if (result.Exception is RasEndpointInactiveException endpointInactive)
            return RasEndpointApiResponses.EndpointInactive<T>(
                endpointInactive.RasEndpointId);

        if (result.Exception is
            RasEndpointGateUnavailableException gateUnavailable)
            return RasEndpointApiResponses.GateUnavailable<T>(
                gateUnavailable.RasEndpointId,
                gateUnavailable.RasGateId);

        if (result.Exception is RasGateNotFoundException gateNotFound)
            return GateNotFound<T>(gateNotFound.RasGateId);

        if (result.Exception is RasClusterNotFoundException clusterNotFound)
            return ClusterNotFound<T>(clusterNotFound.ClusterId);

        return null;
    }

    private static ApiResponse<T> GateInactive<T>(Guid rasGateId)
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.Conflict,
            "ras_gate_inactive",
            $"RasGate '{rasGateId}' is inactive.");
    }
}
