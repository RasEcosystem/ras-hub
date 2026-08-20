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

    public static ApiResponse<ClusterModel> ClusterNotFound(Guid clusterId)
    {
        return ClusterNotFound<ClusterModel>(clusterId);
    }

    public static ApiResponse<T> ClusterNotFound<T>(Guid clusterId)
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.NotFound,
            "ras_cluster_not_found",
            $"RasCluster '{clusterId}' was not found.");
    }

    public static ApiResponse<InfobaseModel> InfobaseNotFound(
        Guid infobaseId)
    {
        return ApiResponse<InfobaseModel>.Fail(
            HttpStatusCode.NotFound,
            "ras_infobase_not_found",
            $"RasInfobase '{infobaseId}' was not found.");
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

    public static ApiResponse<ClusterModel>
        ClusterSynchronizationRejected()
    {
        return ApiResponse<ClusterModel>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_cluster_sync_unavailable",
            "RasGate cluster synchronization could not be scheduled.");
    }

    public static ApiResponse<ClusterModel>
        ClusterSynchronizationFailed(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<ClusterModel>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_cluster_sync_canceled",
                "RasGate cluster synchronization was canceled.");

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<ClusterModel>(inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<ClusterModel>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during cluster synchronization.");

        if (result.Exception is TimeoutException)
            return ApiResponse<ClusterModel>.Fail(
                HttpStatusCode.GatewayTimeout,
                "ras_gate_cluster_timeout",
                "RasGate cluster synchronization timed out.");

        return ApiResponse<ClusterModel>.Fail(
            HttpStatusCode.BadGateway,
            "ras_gate_cluster_sync_failed",
            "RasGate cluster could not be synchronized.");
    }

    public static ApiResponse<PageResult<ClusterModel>>
        ClustersSynchronizationRejected()
    {
        return ApiResponse<PageResult<ClusterModel>>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_clusters_sync_unavailable",
            "RasGate cluster synchronization could not be scheduled.");
    }

    public static ApiResponse<PageResult<ClusterModel>>
        ClustersSynchronizationFailed(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<PageResult<ClusterModel>>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_clusters_sync_canceled",
                "RasGate cluster synchronization was canceled.");

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<PageResult<ClusterModel>>(
                inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<PageResult<ClusterModel>>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during cluster synchronization.");

        if (result.Exception is TimeoutException)
            return ApiResponse<PageResult<ClusterModel>>.Fail(
                HttpStatusCode.GatewayTimeout,
                "ras_gate_clusters_timeout",
                "RasGate cluster synchronization timed out.");

        return ApiResponse<PageResult<ClusterModel>>.Fail(
            HttpStatusCode.BadGateway,
            "ras_gate_clusters_sync_failed",
            "RasGate clusters could not be synchronized.");
    }

    public static ApiResponse<InfobaseModel>
        InfobaseSynchronizationRejected()
    {
        return ApiResponse<InfobaseModel>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_infobase_sync_unavailable",
            "RasGate infobase synchronization could not be scheduled.");
    }

    public static ApiResponse<InfobaseModel>
        InfobaseSynchronizationFailed(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<InfobaseModel>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_infobase_sync_canceled",
                "RasGate infobase synchronization was canceled.");

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<InfobaseModel>(
                inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<InfobaseModel>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during infobase synchronization.");

        if (result.Exception is TimeoutException)
            return ApiResponse<InfobaseModel>.Fail(
                HttpStatusCode.GatewayTimeout,
                "ras_gate_infobase_timeout",
                "RasGate infobase synchronization timed out.");

        return ApiResponse<InfobaseModel>.Fail(
            HttpStatusCode.BadGateway,
            "ras_gate_infobase_sync_failed",
            "RasGate infobase could not be synchronized.");
    }

    public static ApiResponse<PageResult<InfobaseModel>>
        InfobasesSynchronizationRejected()
    {
        return ApiResponse<PageResult<InfobaseModel>>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_infobases_sync_unavailable",
            "RasGate infobase synchronization could not be scheduled.");
    }

    public static ApiResponse<PageResult<InfobaseModel>>
        InfobasesSynchronizationFailed(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<PageResult<InfobaseModel>>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_infobases_sync_canceled",
                "RasGate infobase synchronization was canceled.");

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<PageResult<InfobaseModel>>(
                inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<PageResult<InfobaseModel>>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during infobase synchronization.");

        if (result.Exception is TimeoutException)
            return ApiResponse<PageResult<InfobaseModel>>.Fail(
                HttpStatusCode.GatewayTimeout,
                "ras_gate_infobases_timeout",
                "RasGate infobase synchronization timed out.");

        return ApiResponse<PageResult<InfobaseModel>>.Fail(
            HttpStatusCode.BadGateway,
            "ras_gate_infobases_sync_failed",
            "RasGate infobases could not be synchronized.");
    }

    public static ApiResponse<ClusterModel> ClusterRemovalRejected()
    {
        return ApiResponse<ClusterModel>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_cluster_remove_unavailable",
            "RasGate cluster removal could not be scheduled.");
    }

    public static ApiResponse<ClusterModel> ClusterCreationRejected()
    {
        return ApiResponse<ClusterModel>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_cluster_create_unavailable",
            "RasGate cluster creation could not be scheduled.");
    }

    public static ApiResponse<ClusterModel> ClusterCreationFailed(
        BackgroundTaskResult result)
    {
        return ClusterMutationFailed(
            result,
            "create",
            "creation",
            "RasGate cluster could not be created or its creation could not be confirmed.");
    }

    public static ApiResponse<ClusterModel> ClusterCreationNotPublished()
    {
        return ApiResponse<ClusterModel>.Fail(
            HttpStatusCode.BadGateway,
            "ras_gate_cluster_create_not_published",
            "RasGate cluster creation could not be confirmed. Synchronize clusters before retrying.");
    }

    public static ApiResponse<ClusterModel> ClusterUpdateRejected()
    {
        return ApiResponse<ClusterModel>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_cluster_update_unavailable",
            "RasGate cluster update could not be scheduled.");
    }

    public static ApiResponse<ClusterModel> ClusterUpdateFailed(
        BackgroundTaskResult result)
    {
        return ClusterMutationFailed(
            result,
            "update",
            "update",
            "RasGate cluster could not be updated.");
    }

    public static ApiResponse<ClusterModel> ClusterRemovalFailed(
        BackgroundTaskResult result)
    {
        return ClusterMutationFailed(
            result,
            "remove",
            "removal",
            "RasGate cluster could not be removed.");
    }

    private static ApiResponse<ClusterModel> ClusterMutationFailed(
        BackgroundTaskResult result,
        string operationCode,
        string operationNoun,
        string fallbackMessage)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<ClusterModel>.Fail(
                HttpStatusCode.ServiceUnavailable,
                $"ras_gate_cluster_{operationCode}_canceled",
                $"RasGate cluster {operationNoun} was canceled.");

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<ClusterModel>(inactiveException.RasGateId);

        if (result.Exception is RasGateMutationOutcomeUnknownException)
            return ApiResponse<ClusterModel>.Fail(
                HttpStatusCode.BadGateway,
                $"ras_gate_cluster_{operationCode}_outcome_unknown",
                $"RasGate could not confirm the cluster {operationNoun} " +
                "outcome. Synchronize cluster state before retrying.");

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<ClusterModel>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                $"RasGate configuration changed during cluster {operationNoun}.");

        if (result.Exception is TimeoutException)
            return ApiResponse<ClusterModel>.Fail(
                HttpStatusCode.GatewayTimeout,
                $"ras_gate_cluster_{operationCode}_timeout",
                $"RasGate cluster {operationNoun} timed out.");

        return ApiResponse<ClusterModel>.Fail(
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