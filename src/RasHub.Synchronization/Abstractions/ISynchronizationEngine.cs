using RasHub.Synchronization.Models;

namespace RasHub.Synchronization.Abstractions;

/// <summary>
///     Public entry point for enqueueing, canceling, and observing background task executions.
/// </summary>
public interface ISynchronizationEngine
{
    BackgroundTaskHandle Enqueue<TTask>(
        TTask task,
        BackgroundTaskOptions? options = null)
        where TTask : IBackgroundTask;

    bool Cancel(Guid taskId);

    BackgroundTaskSnapshot? GetTask(Guid taskId);

    IReadOnlyList<BackgroundTaskSnapshot> GetTasks();

    SynchronizationEngineStatistics GetStatistics();
}