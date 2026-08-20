using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.Abstractions;

/// <summary>
///     Public entry point for enqueueing, canceling, and observing background task executions.
/// </summary>
public interface IBackgroundTaskEngine
{
    BackgroundTaskHandle Enqueue<TTask>(
        TTask task,
        BackgroundTaskOptions? options = null)
        where TTask : IBackgroundTask;

    bool Cancel(Guid taskId);

    BackgroundTaskSnapshot? GetTask(Guid taskId);

    IReadOnlyList<BackgroundTaskSnapshot> GetTasks();

    BackgroundTaskEngineStatistics GetStatistics();
}
