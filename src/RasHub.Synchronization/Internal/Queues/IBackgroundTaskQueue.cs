namespace RasHub.Synchronization.Internal;

internal interface IBackgroundTaskQueue
{
    bool TryEnqueue(BackgroundTaskExecution execution);

    ValueTask<BackgroundTaskExecution> DequeueAsync(
        BackgroundTaskQueue queue,
        CancellationToken cancellationToken);

    int GetCount(BackgroundTaskQueue queue);
}