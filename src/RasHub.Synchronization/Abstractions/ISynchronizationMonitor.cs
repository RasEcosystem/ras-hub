using RasHub.Synchronization.Models;

namespace RasHub.Synchronization.Abstractions;

/// <summary>
///     Provides a read-only diagnostic view of the Synchronization Engine without exposing control operations.
/// </summary>
public interface ISynchronizationMonitor
{
    SynchronizationMonitorSnapshot GetSnapshot();

    BackgroundTaskSnapshot? GetTask(Guid taskId);

    IReadOnlyList<BackgroundTaskSnapshot> GetTasks(
        BackgroundTaskState? state = null,
        BackgroundTaskQueue? queue = null,
        int limit = 100);

    IReadOnlyList<BackgroundTaskScheduleSnapshot> GetSchedules();
}