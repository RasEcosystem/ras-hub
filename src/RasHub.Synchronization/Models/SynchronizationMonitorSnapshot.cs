namespace RasHub.Synchronization.Models;

/// <summary>
///     Point-in-time diagnostic view of queues, executions, concurrency keys, and periodic schedules.
/// </summary>
public sealed record SynchronizationMonitorSnapshot(
    DateTimeOffset CapturedAt,
    SynchronizationEngineSettingsSnapshot Settings,
    SynchronizationEngineStatistics Statistics,
    IReadOnlyDictionary<BackgroundTaskState, int> TasksByState,
    int DelayedTaskCount,
    int ActiveConcurrencyKeyCount,
    IReadOnlyList<BackgroundTaskScheduleSnapshot> Schedules,
    IReadOnlyList<BackgroundTaskSnapshot> RunningTasks,
    IReadOnlyList<BackgroundTaskSnapshot> RecentFailures);