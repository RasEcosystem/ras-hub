namespace RasHub.BackgroundTasks.Internal.Processing;

/// <summary>
///     Runs engine-owned maintenance and coordinates cancellation during host shutdown.
/// </summary>
internal interface IBackgroundTaskEngineLifecycle
{
    Task RunCompletedTaskCleanupAsync(CancellationToken stoppingToken);

    void StopAcceptingAndCancelAll();

    Task DrainCancellationSignalsAsync();
}
