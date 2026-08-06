namespace RasHub.Synchronization.Models;

/// <summary>
///     Current registry size and waiting-task count for each queue lane.
/// </summary>
public sealed record SynchronizationEngineStatistics(
    int TrackedTasks,
    int InteractiveQueueLength,
    int SynchronizationQueueLength,
    int MaintenanceQueueLength);