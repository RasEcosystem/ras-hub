using Microsoft.Extensions.DependencyInjection;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Exceptions;

namespace RasHub.BackgroundTasks.Internal.Execution;

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

    public async Task<object?> InvokeAsync(
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

        await handler.ExecuteAsync(
            typedTask,
            cancellationToken);

        return null;
    }
}

/// <summary>
///     Bridges a result-producing runtime task to its strongly typed handler.
/// </summary>
internal sealed class BackgroundTaskResultInvoker<TTask, TResult>
    : IBackgroundTaskInvoker
    where TTask : IBackgroundTask<TResult>
{
    private BackgroundTaskResultInvoker()
    {
    }

    public async Task<object?> InvokeAsync(
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
            .GetService<IBackgroundTaskHandler<TTask, TResult>>();

        if (handler is null)
            throw new NonRetryableBackgroundTaskException(
                $"No handler is registered for background task " +
                $"'{typeof(TTask).FullName}'.");

        return await handler.ExecuteAsync(
            typedTask,
            cancellationToken);
    }
}