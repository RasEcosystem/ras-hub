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