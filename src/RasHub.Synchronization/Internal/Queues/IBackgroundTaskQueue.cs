using RasHub.Synchronization.Internal.Execution;
using RasHub.Synchronization.Models;

namespace RasHub.Synchronization.Internal.Queues;

/// <summary>
///     Bounded queue abstraction that isolates interactive, synchronization, and maintenance lanes.
/// </summary>
internal interface IBackgroundTaskQueue
{
    bool TryEnqueue(BackgroundTaskExecution execution);

    ValueTask<BackgroundTaskExecution> DequeueAsync(
        BackgroundTaskQueue queue,
        CancellationToken cancellationToken);

    int GetCount(BackgroundTaskQueue queue);
}