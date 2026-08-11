using System.Net;
using RasHub.Application.RasGates.Exceptions;
using RasHub.BackgroundTasks.Models;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Responses;

namespace RasHub.Web.Api.RasGates;

internal static class RasGateApiResponses
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

    public static ApiResponse<RasClusterModel> ClusterNotFound(Guid clusterId)
    {
        return ApiResponse<RasClusterModel>.Fail(
            HttpStatusCode.NotFound,
            "ras_cluster_not_found",
            $"RasCluster '{clusterId}' was not found.");
    }

    public static ApiResponse<RasGateStatusResponse>
        StatusSynchronizationRejected()
    {
        return ApiResponse<RasGateStatusResponse>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_status_sync_unavailable",
            "RasGate status synchronization could not be scheduled.");
    }

    public static ApiResponse<RasGateStatusResponse>
        StatusSynchronizationFailed(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<RasGateStatusResponse>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_status_sync_canceled",
                "RasGate status synchronization was canceled.");

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<RasGateStatusResponse>(
                inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<RasGateStatusResponse>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during status synchronization.");

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

    public static ApiResponse<RasClusterModel>
        ClusterSynchronizationRejected()
    {
        return ApiResponse<RasClusterModel>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_cluster_sync_unavailable",
            "RasGate cluster synchronization could not be scheduled.");
    }

    public static ApiResponse<RasClusterModel>
        ClusterSynchronizationFailed(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<RasClusterModel>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_cluster_sync_canceled",
                "RasGate cluster synchronization was canceled.");

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<RasClusterModel>(inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<RasClusterModel>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during cluster synchronization.");

        if (result.Exception is TimeoutException)
            return ApiResponse<RasClusterModel>.Fail(
                HttpStatusCode.GatewayTimeout,
                "ras_gate_cluster_timeout",
                "RasGate cluster synchronization timed out.");

        return ApiResponse<RasClusterModel>.Fail(
            HttpStatusCode.BadGateway,
            "ras_gate_cluster_sync_failed",
            "RasGate cluster could not be synchronized.");
    }

    public static ApiResponse<PageResult<RasClusterModel>>
        ClustersSynchronizationRejected()
    {
        return ApiResponse<PageResult<RasClusterModel>>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_clusters_sync_unavailable",
            "RasGate cluster synchronization could not be scheduled.");
    }

    public static ApiResponse<PageResult<RasClusterModel>>
        ClustersSynchronizationFailed(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<PageResult<RasClusterModel>>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_clusters_sync_canceled",
                "RasGate cluster synchronization was canceled.");

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<PageResult<RasClusterModel>>(
                inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<PageResult<RasClusterModel>>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during cluster synchronization.");

        if (result.Exception is TimeoutException)
            return ApiResponse<PageResult<RasClusterModel>>.Fail(
                HttpStatusCode.GatewayTimeout,
                "ras_gate_clusters_timeout",
                "RasGate cluster synchronization timed out.");

        return ApiResponse<PageResult<RasClusterModel>>.Fail(
            HttpStatusCode.BadGateway,
            "ras_gate_clusters_sync_failed",
            "RasGate clusters could not be synchronized.");
    }

    public static ApiResponse<RasClusterModel> ClusterRemovalRejected()
    {
        return ApiResponse<RasClusterModel>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_cluster_remove_unavailable",
            "RasGate cluster removal could not be scheduled.");
    }

    public static ApiResponse<RasClusterModel> ClusterCreationRejected()
    {
        return ApiResponse<RasClusterModel>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_cluster_create_unavailable",
            "RasGate cluster creation could not be scheduled.");
    }

    public static ApiResponse<RasClusterModel> ClusterCreationFailed(
        BackgroundTaskResult result)
    {
        return ClusterMutationFailed(
            result,
            "create",
            "creation",
            "RasGate cluster could not be created or its creation could not be confirmed.");
    }

    public static ApiResponse<RasClusterModel> ClusterCreationNotPublished()
    {
        return ApiResponse<RasClusterModel>.Fail(
            HttpStatusCode.BadGateway,
            "ras_gate_cluster_create_not_published",
            "RasGate cluster creation could not be confirmed. Synchronize clusters before retrying.");
    }

    public static ApiResponse<RasClusterModel> ClusterUpdateRejected()
    {
        return ApiResponse<RasClusterModel>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_cluster_update_unavailable",
            "RasGate cluster update could not be scheduled.");
    }

    public static ApiResponse<RasClusterModel> ClusterUpdateFailed(
        BackgroundTaskResult result)
    {
        return ClusterMutationFailed(
            result,
            "update",
            "update",
            "RasGate cluster could not be updated.");
    }

    public static ApiResponse<RasClusterModel> ClusterRemovalFailed(
        BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<RasClusterModel>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_cluster_remove_canceled",
                "RasGate cluster removal was canceled.");

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<RasClusterModel>(inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<RasClusterModel>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during cluster removal.");

        if (result.Exception is TimeoutException)
            return ApiResponse<RasClusterModel>.Fail(
                HttpStatusCode.GatewayTimeout,
                "ras_gate_cluster_remove_timeout",
                "RasGate cluster removal timed out.");

        return ApiResponse<RasClusterModel>.Fail(
            HttpStatusCode.BadGateway,
            "ras_gate_cluster_remove_failed",
            "RasGate cluster could not be removed.");
    }

    private static ApiResponse<RasClusterModel> ClusterMutationFailed(
        BackgroundTaskResult result,
        string operationCode,
        string operationNoun,
        string fallbackMessage)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<RasClusterModel>.Fail(
                HttpStatusCode.ServiceUnavailable,
                $"ras_gate_cluster_{operationCode}_canceled",
                $"RasGate cluster {operationNoun} was canceled.");

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<RasClusterModel>(inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<RasClusterModel>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                $"RasGate configuration changed during cluster {operationNoun}.");

        if (result.Exception is TimeoutException)
            return ApiResponse<RasClusterModel>.Fail(
                HttpStatusCode.GatewayTimeout,
                $"ras_gate_cluster_{operationCode}_timeout",
                $"RasGate cluster {operationNoun} timed out.");

        return ApiResponse<RasClusterModel>.Fail(
            HttpStatusCode.BadGateway,
            $"ras_gate_cluster_{operationCode}_failed",
            fallbackMessage);
    }

    private static ApiResponse<T> GateInactive<T>(Guid rasGateId)
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.Conflict,
            "ras_gate_inactive",
            $"RasGate '{rasGateId}' is inactive.");
    }
}