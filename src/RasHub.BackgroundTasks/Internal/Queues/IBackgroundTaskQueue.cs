using RasHub.BackgroundTasks.Internal.Execution;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.Internal.Queues;

internal interface IBackgroundTaskQueue
{
    bool TryEnqueue(BackgroundTaskExecution execution);

    ValueTask<BackgroundTaskExecution> DequeueAsync(
        BackgroundTaskQueue queue,
        CancellationToken cancellationToken);

    int GetCount(BackgroundTaskQueue queue);

    int GetHighWaterMark(BackgroundTaskQueue queue);
}