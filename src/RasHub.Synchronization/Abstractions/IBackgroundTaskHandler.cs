namespace RasHub.Synchronization.Abstractions;

/// <summary>
///     Contains the business logic for one background task type and is resolved from a fresh DI scope per attempt.
/// </summary>
public interface IBackgroundTaskHandler<in TTask>
    where TTask : IBackgroundTask
{
    Task ExecuteAsync(
        TTask task,
        CancellationToken cancellationToken);
}