using RasHub.BackgroundTasks.Models;

namespace RasHub.Web.Infrastructure.RasGates;

internal static class RasGateTaskOptions
{
    private const int MaxAttempts = 2;

    public static BackgroundTaskOptions InteractiveStatusSynchronization(
        Guid rasGateId)
    {
        return Interactive(
            TimeSpan.FromSeconds(10),
            $"ras-gate-status:{rasGateId}",
            rasGateId);
    }

    public static BackgroundTaskOptions StatusMonitoring(
        Guid rasGateId,
        TimeSpan requestTimeout)
    {
        return new BackgroundTaskOptions
        {
            Queue = BackgroundTaskQueue.Synchronization,
            MaxAttempts = MaxAttempts,
            RetryDelay = TimeSpan.FromSeconds(1),
            Timeout = requestTimeout,
            DeduplicationKey = $"ras-gate-status:{rasGateId}",
            ConcurrencyKey = $"ras-gate:{rasGateId}"
        };
    }

    public static BackgroundTaskOptions InteractiveClusterSynchronization(
        Guid rasEndpointId,
        Guid clusterId)
    {
        return Interactive(
            TimeSpan.FromSeconds(30),
            $"ras-endpoint-cluster:{rasEndpointId}:{clusterId}",
            rasEndpointId,
            true);
    }

    public static BackgroundTaskOptions InteractiveClustersSynchronization(
        Guid rasEndpointId)
    {
        return Interactive(
            TimeSpan.FromSeconds(30),
            $"ras-endpoint-clusters:{rasEndpointId}",
            rasEndpointId,
            true);
    }

    public static BackgroundTaskOptions InteractiveInfobasesSynchronization(
        Guid rasEndpointId,
        Guid clusterId)
    {
        return Interactive(
            TimeSpan.FromSeconds(30),
            $"ras-endpoint-infobases:{rasEndpointId}:{clusterId}",
            rasEndpointId,
            true);
    }

    public static BackgroundTaskOptions InteractiveInfobaseSynchronization(
        Guid rasEndpointId,
        Guid clusterId,
        Guid infobaseId)
    {
        return Interactive(
            TimeSpan.FromSeconds(30),
            $"ras-endpoint-infobase:{rasEndpointId}:{clusterId}:{infobaseId}",
            rasEndpointId,
            true);
    }

    public static BackgroundTaskOptions InteractiveClusterRemoval(
        Guid rasEndpointId,
        Guid clusterId)
    {
        return new BackgroundTaskOptions
        {
            Queue = BackgroundTaskQueue.Interactive,
            MaxAttempts = 1,
            Timeout = TimeSpan.FromSeconds(30),
            DeduplicationKey =
                $"ras-endpoint-cluster-remove:{rasEndpointId}:{clusterId}",
            ConcurrencyKey = $"ras-endpoint:{rasEndpointId}"
        };
    }

    public static BackgroundTaskOptions InteractiveClusterCreation(
        Guid rasEndpointId)
    {
        return InteractiveClusterMutation(rasEndpointId);
    }

    public static BackgroundTaskOptions InteractiveClusterUpdate(
        Guid rasEndpointId)
    {
        return InteractiveClusterMutation(rasEndpointId);
    }

    private static BackgroundTaskOptions InteractiveClusterMutation(
        Guid rasEndpointId)
    {
        return new BackgroundTaskOptions
        {
            Queue = BackgroundTaskQueue.Interactive,
            MaxAttempts = 1,
            Timeout = TimeSpan.FromSeconds(30),
            ConcurrencyKey = $"ras-endpoint:{rasEndpointId}"
        };
    }

    private static BackgroundTaskOptions Interactive(
        TimeSpan timeout,
        string deduplicationKey,
        Guid ownerId,
        bool endpointScoped = false)
    {
        return new BackgroundTaskOptions
        {
            Queue = BackgroundTaskQueue.Interactive,
            MaxAttempts = MaxAttempts,
            RetryDelay = TimeSpan.FromMilliseconds(250),
            Timeout = timeout,
            DeduplicationKey = deduplicationKey,
            ConcurrencyKey = endpointScoped
                ? $"ras-endpoint:{ownerId}"
                : $"ras-gate:{ownerId}"
        };
    }
}
