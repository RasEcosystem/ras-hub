using System.Net;
using RasHub.Application.RasGates.Exceptions;
using RasHub.BackgroundTasks.Models;
using RasHub.Contracts.Common;
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
            "cluster_not_found",
            $"Cluster '{clusterId}' was not found.");
    }

    public static ApiResponse<InfobaseModel> InfobaseNotFound(
        Guid infobaseId)
    {
        return ApiResponse<InfobaseModel>.Fail(
            HttpStatusCode.NotFound,
            "infobase_not_found",
            $"Infobase '{infobaseId}' was not found.");
    }

    public static ApiResponse<RasGateStatusResponse>
        StatusSynchronizationRejected()
    {
        return ApiResponse<RasGateStatusResponse>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "ras_gate_status_synchronization_unavailable",
            "RasGate status synchronization could not be scheduled.");
    }

    public static ApiResponse<RasGateStatusResponse>
        StatusSynchronizationFailed(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<RasGateStatusResponse>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_status_synchronization_canceled",
                "RasGate status synchronization was canceled.");

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
            "cluster_synchronization_unavailable",
            "Cluster synchronization could not be scheduled.");
    }

    public static ApiResponse<ClusterModel>
        ClusterSynchronizationFailed(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<ClusterModel>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "cluster_synchronization_canceled",
                "Cluster synchronization was canceled.");

        if (TryMapLocalStateFailure<ClusterModel>(result) is
            { } localStateFailure)
            return localStateFailure;

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<ClusterModel>(inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<ClusterModel>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during cluster synchronization.");

        if (result.Exception is RacResourceNotFoundException
            {
                Resource: "clusters"
            } notFoundException)
            return ClusterNotFound(notFoundException.ExternalId);

        if (TryMapRacFailure<ClusterModel>(result) is { } racFailure)
            return racFailure;

        if (result.Exception is TimeoutException)
            return ApiResponse<ClusterModel>.Fail(
                HttpStatusCode.GatewayTimeout,
                "cluster_synchronization_timeout",
                "Cluster synchronization through RasGate timed out.");

        return ApiResponse<ClusterModel>.Fail(
            HttpStatusCode.BadGateway,
            "cluster_synchronization_failed",
            "Cluster could not be synchronized through RasGate.");
    }

    public static ApiResponse<CollectionSynchronizationResponse>
        ClustersSynchronizationRejected()
    {
        return ApiResponse<CollectionSynchronizationResponse>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "clusters_synchronization_unavailable",
            "Cluster synchronization could not be scheduled.");
    }

    public static ApiResponse<CollectionSynchronizationResponse>
        ClustersSynchronizationFailed(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<CollectionSynchronizationResponse>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "clusters_synchronization_canceled",
                "Cluster synchronization was canceled.");

        if (TryMapLocalStateFailure<CollectionSynchronizationResponse>(result)
            is { } localStateFailure)
            return localStateFailure;

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<CollectionSynchronizationResponse>(
                inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<CollectionSynchronizationResponse>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during cluster synchronization.");

        if (TryMapRacFailure<CollectionSynchronizationResponse>(result) is
            { } racFailure)
            return racFailure;

        if (result.Exception is TimeoutException)
            return ApiResponse<CollectionSynchronizationResponse>.Fail(
                HttpStatusCode.GatewayTimeout,
                "clusters_synchronization_timeout",
                "Cluster synchronization through RasGate timed out.");

        return ApiResponse<CollectionSynchronizationResponse>.Fail(
            HttpStatusCode.BadGateway,
            "clusters_synchronization_failed",
            "Clusters could not be synchronized through RasGate.");
    }

    public static ApiResponse<InfobaseModel>
        InfobaseSynchronizationRejected()
    {
        return ApiResponse<InfobaseModel>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "infobase_synchronization_unavailable",
            "Infobase synchronization could not be scheduled.");
    }

    public static ApiResponse<InfobaseModel>
        InfobaseSynchronizationFailed(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<InfobaseModel>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "infobase_synchronization_canceled",
                "Infobase synchronization was canceled.");

        if (TryMapLocalStateFailure<InfobaseModel>(result) is
            { } localStateFailure)
            return localStateFailure;

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<InfobaseModel>(
                inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<InfobaseModel>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during infobase synchronization.");

        if (result.Exception is RacResourceNotFoundException
            {
                Resource: "infobases"
            } notFoundException)
            return InfobaseNotFound(notFoundException.ExternalId);

        if (TryMapRacFailure<InfobaseModel>(result) is { } racFailure)
            return racFailure;

        if (result.Exception is TimeoutException)
            return ApiResponse<InfobaseModel>.Fail(
                HttpStatusCode.GatewayTimeout,
                "infobase_synchronization_timeout",
                "Infobase synchronization through RasGate timed out.");

        return ApiResponse<InfobaseModel>.Fail(
            HttpStatusCode.BadGateway,
            "infobase_synchronization_failed",
            "Infobase could not be synchronized through RasGate.");
    }

    public static ApiResponse<CollectionSynchronizationResponse>
        InfobasesSynchronizationRejected()
    {
        return ApiResponse<CollectionSynchronizationResponse>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "infobases_synchronization_unavailable",
            "Infobase synchronization could not be scheduled.");
    }

    public static ApiResponse<CollectionSynchronizationResponse>
        InfobasesSynchronizationFailed(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<CollectionSynchronizationResponse>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "infobases_synchronization_canceled",
                "Infobase synchronization was canceled.");

        if (TryMapLocalStateFailure<CollectionSynchronizationResponse>(result)
            is { } localStateFailure)
            return localStateFailure;

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<CollectionSynchronizationResponse>(
                inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<CollectionSynchronizationResponse>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during infobase synchronization.");

        if (TryMapRacFailure<CollectionSynchronizationResponse>(result) is
            { } racFailure)
            return racFailure;

        if (result.Exception is TimeoutException)
            return ApiResponse<CollectionSynchronizationResponse>.Fail(
                HttpStatusCode.GatewayTimeout,
                "infobases_synchronization_timeout",
                "Infobase synchronization through RasGate timed out.");

        return ApiResponse<CollectionSynchronizationResponse>.Fail(
            HttpStatusCode.BadGateway,
            "infobases_synchronization_failed",
            "Infobases could not be synchronized through RasGate.");
    }

    public static ApiResponse<ClusterModel> ClusterRemovalRejected()
    {
        return ApiResponse<ClusterModel>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "cluster_remove_unavailable",
            "Cluster removal could not be scheduled.");
    }

    public static ApiResponse<ClusterModel> ClusterCreationRejected()
    {
        return ApiResponse<ClusterModel>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "cluster_create_unavailable",
            "Cluster creation could not be scheduled.");
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

    public static ApiResponse<ClusterModel> ClusterCreationNotConfirmed()
    {
        return ClusterMutationNotConfirmed("create", "creation");
    }

    public static ApiResponse<ClusterModel> ClusterUpdateRejected()
    {
        return ApiResponse<ClusterModel>.Fail(
            HttpStatusCode.ServiceUnavailable,
            "cluster_update_unavailable",
            "Cluster update could not be scheduled.");
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

    public static ApiResponse<ClusterModel> ClusterUpdateNotConfirmed()
    {
        return ClusterMutationNotConfirmed("update", "update");
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
                $"cluster_{operationCode}_canceled",
                $"Cluster {operationNoun} was canceled.");

        if (TryMapLocalStateFailure<ClusterModel>(result) is
            { } localStateFailure)
            return localStateFailure;

        if (result.Exception is RasGateInactiveException inactiveException)
            return GateInactive<ClusterModel>(inactiveException.RasGateId);

        if (result.Exception is RasGateMutationOutcomeUnknownException)
            return ApiResponse<ClusterModel>.Fail(
                HttpStatusCode.BadGateway,
                $"cluster_{operationCode}_outcome_unknown",
                $"RasGate could not confirm the cluster {operationNoun} " +
                "outcome. Synchronize cluster state and verify the target " +
                "RasGate. Do not retry the mutation automatically.");

        if (result.Exception is
            RasGateMutationReadBackNotConfirmedException
            {
                Resource: "clusters"
            } or
            RasGateMutationPublicationNotConfirmedException
            {
                Resource: "clusters"
            })
            return ClusterMutationNotConfirmed(operationCode, operationNoun);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<ClusterModel>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                $"RasGate configuration changed during cluster {operationNoun}.");

        if (TryMapRacFailure<ClusterModel>(result) is { } racFailure)
            return racFailure;

        if (result.Exception is TimeoutException)
            return ApiResponse<ClusterModel>.Fail(
                HttpStatusCode.GatewayTimeout,
                $"cluster_{operationCode}_timeout",
                $"Cluster {operationNoun} through RasGate timed out.");

        return ApiResponse<ClusterModel>.Fail(
            HttpStatusCode.BadGateway,
            $"cluster_{operationCode}_failed",
            fallbackMessage);
    }

    private static ApiResponse<ClusterModel> ClusterMutationNotConfirmed(
        string operationCode,
        string operationNoun)
    {
        return ApiResponse<ClusterModel>.Fail(
            HttpStatusCode.BadGateway,
            $"cluster_{operationCode}_not_confirmed",
            $"Cluster {operationNoun} could not be confirmed. " +
            "Synchronize cluster state and verify the target RasGate. " +
            "Do not retry the mutation automatically.");
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
