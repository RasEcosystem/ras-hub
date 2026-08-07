using RasHub.Synchronization.Internal.Execution;
using RasHub.Synchronization.Models;

namespace RasHub.Synchronization.Internal.Queues;

internal interface IBackgroundTaskQueue
{
    bool TryEnqueue(BackgroundTaskExecution execution);

    ValueTask<BackgroundTaskExecution> DequeueAsync(
        BackgroundTaskQueue queue,
        CancellationToken cancellationToken);

    int GetCount(BackgroundTaskQueue queue);

    int GetHighWaterMark(BackgroundTaskQueue queue);
}
