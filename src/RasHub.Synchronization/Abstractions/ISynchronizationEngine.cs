namespace RasHub.Synchronization;

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