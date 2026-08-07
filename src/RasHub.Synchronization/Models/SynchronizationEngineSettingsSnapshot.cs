namespace RasHub.Synchronization.Models;

/// <summary>
///     Read-only diagnostic view of the configured queue capacities and worker quotas.
/// </summary>
public sealed record SynchronizationEngineSettingsSnapshot(
    int MaxTrackedTasks,
    int InteractiveQueueCapacity,
    int SynchronizationQueueCapacity,
    int MaintenanceQueueCapacity,
    int InteractiveWorkerCount,
    int SynchronizationWorkerCount,
    int MaintenanceWorkerCount);