namespace RasHub.BackgroundTasks.Internal.Processing;

/// <summary>Closes task admission and requests cancellation of all tracked work.</summary>
internal interface IBackgroundTaskEngineLifecycle
{
    void StopAcceptingAndCancelAll();

    Task DrainCancellationSignalsAsync();
}