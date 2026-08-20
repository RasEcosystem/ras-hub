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
        Guid rasGateId,
        Guid clusterId)
    {
        return Interactive(
            TimeSpan.FromSeconds(30),
            $"ras-gate-cluster:{rasGateId}:{clusterId}",
            rasGateId);
    }

    public static BackgroundTaskOptions InteractiveClustersSynchronization(
        Guid rasGateId)
    {
        return Interactive(
            TimeSpan.FromSeconds(30),
            $"ras-gate-clusters:{rasGateId}",
            rasGateId);
    }

    public static BackgroundTaskOptions InteractiveInfobasesSynchronization(
        Guid rasGateId,
        Guid clusterId)
    {
        return Interactive(
            TimeSpan.FromSeconds(30),
            $"ras-gate-infobases:{rasGateId}:{clusterId}",
            rasGateId);
    }

    public static BackgroundTaskOptions InteractiveInfobaseSynchronization(
        Guid rasGateId,
        Guid clusterId,
        Guid infobaseId)
    {
        return Interactive(
            TimeSpan.FromSeconds(30),
            $"ras-gate-infobase:{rasGateId}:{clusterId}:{infobaseId}",
            rasGateId);
    }

    public static BackgroundTaskOptions InteractiveClusterRemoval(
        Guid rasGateId,
        Guid clusterId)
    {
        return new BackgroundTaskOptions
        {
            Queue = BackgroundTaskQueue.Interactive,
            MaxAttempts = 1,
            Timeout = TimeSpan.FromSeconds(30),
            DeduplicationKey =
                $"ras-gate-cluster-remove:{rasGateId}:{clusterId}",
            ConcurrencyKey = $"ras-gate:{rasGateId}"
        };
    }

    public static BackgroundTaskOptions InteractiveClusterCreation(
        Guid rasGateId)
    {
        return InteractiveClusterMutation(rasGateId);
    }

    public static BackgroundTaskOptions InteractiveClusterUpdate(
        Guid rasGateId)
    {
        return InteractiveClusterMutation(rasGateId);
    }

    private static BackgroundTaskOptions InteractiveClusterMutation(
        Guid rasGateId)
    {
        return new BackgroundTaskOptions
        {
            Queue = BackgroundTaskQueue.Interactive,
            MaxAttempts = 1,
            Timeout = TimeSpan.FromSeconds(30),
            ConcurrencyKey = $"ras-gate:{rasGateId}"
        };
    }

    private static BackgroundTaskOptions Interactive(
        TimeSpan timeout,
        string deduplicationKey,
        Guid rasGateId)
    {
        return new BackgroundTaskOptions
        {
            Queue = BackgroundTaskQueue.Interactive,
            MaxAttempts = MaxAttempts,
            RetryDelay = TimeSpan.FromMilliseconds(250),
            Timeout = timeout,
            DeduplicationKey = deduplicationKey,
            ConcurrencyKey = $"ras-gate:{rasGateId}"
        };
    }
}