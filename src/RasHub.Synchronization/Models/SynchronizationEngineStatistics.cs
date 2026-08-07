namespace RasHub.Synchronization.Models;

/// <summary>
///     Current active work, retained history, and queue statistics.
/// </summary>
public sealed record SynchronizationEngineStatistics(
    int ActiveTasks,
    int CompletedTaskHistory,
    int InteractiveQueueLength,
    int SynchronizationQueueLength,
    int MaintenanceQueueLength,
    long InteractiveCompletedTasks,
    long SynchronizationCompletedTasks,
    long MaintenanceCompletedTasks,
    int InteractiveQueueHighWaterMark,
    int SynchronizationQueueHighWaterMark,
    int MaintenanceQueueHighWaterMark,
    BackgroundTaskTimingStatistics OverallTiming,
    BackgroundTaskTimingStatistics InteractiveTiming,
    BackgroundTaskTimingStatistics SynchronizationTiming,
    BackgroundTaskTimingStatistics MaintenanceTiming,
    DateTimeOffset StartedAt);