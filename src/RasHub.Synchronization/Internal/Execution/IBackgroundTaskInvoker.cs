using RasHub.Synchronization.Abstractions;

namespace RasHub.Synchronization.Internal.Execution;

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