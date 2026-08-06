namespace RasHub.Synchronization;

public interface IBackgroundTaskHandler<in TTask>
    where TTask : IBackgroundTask
{
    Task ExecuteAsync(
        TTask task,
        CancellationToken cancellationToken);
}