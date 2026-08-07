using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Exceptions;
using RasHub.Synchronization.Tasks;

namespace RasHub.Synchronization.Internal.Execution;

internal sealed class TestBackgroundTaskHandler
    : IBackgroundTaskHandler<TestBackgroundTask>
{
    public Task ExecuteAsync(
        TestBackgroundTask task,
        CancellationToken cancellationToken)
    {
        if (task.Duration < TimeSpan.Zero)
            throw new NonRetryableBackgroundTaskException(
                "Test task duration cannot be negative.");

        return Task.Delay(task.Duration, cancellationToken);
    }
}
