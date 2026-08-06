using Microsoft.Extensions.DependencyInjection;
using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Exceptions;

namespace RasHub.Synchronization.Internal.Execution;

/// <summary>
///     Bridges a runtime task object to its strongly typed <see cref="IBackgroundTaskHandler{TTask}" />.
/// </summary>
internal sealed class BackgroundTaskInvoker<TTask>
    : IBackgroundTaskInvoker
    where TTask : IBackgroundTask
{
    private BackgroundTaskInvoker()
    {
    }

    public Task InvokeAsync(
        IServiceProvider serviceProvider,
        IBackgroundTask backgroundTask,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(backgroundTask);

        if (backgroundTask is not TTask typedTask)
            throw new InvalidOperationException(
                $"Expected task type '{typeof(TTask).FullName}', " +
                $"but received '{backgroundTask.GetType().FullName}'.");

        var handler = serviceProvider
            .GetService<IBackgroundTaskHandler<TTask>>();

        if (handler is null)
            throw new NonRetryableBackgroundTaskException(
                $"No handler is registered for background task " +
                $"'{typeof(TTask).FullName}'.");

        return handler.ExecuteAsync(
            typedTask,
            cancellationToken);
    }
}