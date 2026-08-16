using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.BackgroundTasks.Models;

namespace RasHub.Web.Api.RasGates;

public sealed class InteractiveTaskRunner(IBackgroundTaskEngine taskEngine)
{
    public async Task<InteractiveTaskExecution> RunAsync<TTask>(
        TTask task,
        BackgroundTaskOptions options,
        CancellationToken cancellationToken)
        where TTask : IBackgroundTask
    {
        BackgroundTaskHandle handle;

        try
        {
            handle = taskEngine.Enqueue(task, options);
        }
        catch (BackgroundTaskRejectedException)
        {
            return InteractiveTaskExecution.Rejected();
        }

        var result = await handle.WaitAsync(cancellationToken);

        return InteractiveTaskExecution.Completed(result);
    }

    public async Task<InteractiveTaskExecution<TResult>> RunWithResultAsync<
        TTask,
        TResult>(
        TTask task,
        BackgroundTaskOptions options,
        CancellationToken cancellationToken)
        where TTask : IBackgroundTask<TResult>
    {
        BackgroundTaskHandle handle;

        try
        {
            handle = taskEngine.Enqueue(task, options);
        }
        catch (BackgroundTaskRejectedException)
        {
            return InteractiveTaskExecution<TResult>.Rejected();
        }

        var result = await handle.WaitAsync(cancellationToken);

        return result.IsSucceeded
            ? InteractiveTaskExecution<TResult>.Completed(
                result,
                result.GetValue<TResult>())
            : InteractiveTaskExecution<TResult>.Completed(result, default);
    }
}

public sealed record InteractiveTaskExecution(
    bool WasRejected,
    BackgroundTaskResult? Result)
{
    public static InteractiveTaskExecution Rejected()
    {
        return new InteractiveTaskExecution(true, null);
    }

    public static InteractiveTaskExecution Completed(BackgroundTaskResult result)
    {
        return new InteractiveTaskExecution(false, result);
    }
}

public sealed record InteractiveTaskExecution<TResult>(
    bool WasRejected,
    BackgroundTaskResult? Result,
    TResult? Value)
{
    public static InteractiveTaskExecution<TResult> Rejected()
    {
        return new InteractiveTaskExecution<TResult>(true, null, default);
    }

    public static InteractiveTaskExecution<TResult> Completed(
        BackgroundTaskResult result,
        TResult? value)
    {
        return new InteractiveTaskExecution<TResult>(false, result, value);
    }
}