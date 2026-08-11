namespace RasHub.BackgroundTasks.Abstractions;

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

/// <summary>
///     Contains business logic for one result-producing background task type.
/// </summary>
public interface IBackgroundTaskHandler<in TTask, TResult>
    where TTask : IBackgroundTask<TResult>
{
    Task<TResult> ExecuteAsync(
        TTask task,
        CancellationToken cancellationToken);
}