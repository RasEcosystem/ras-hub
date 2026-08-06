namespace RasHub.Synchronization.Internal;

internal interface IBackgroundTaskInvoker
{
    Task InvokeAsync(
        IServiceProvider serviceProvider,
        IBackgroundTask backgroundTask,
        CancellationToken cancellationToken);
}