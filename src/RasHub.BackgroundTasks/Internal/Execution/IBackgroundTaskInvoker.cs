using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.BackgroundTasks.Internal.Execution;

/// <summary>
///     Non-generic dispatch contract used after the Engine discovers a task's type at runtime.
/// </summary>
internal interface IBackgroundTaskInvoker
{
    Task InvokeAsync(
        IServiceProvider serviceProvider,
        IBackgroundTask backgroundTask,
        CancellationToken cancellationToken);
}