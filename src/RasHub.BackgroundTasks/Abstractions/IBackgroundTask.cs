namespace RasHub.BackgroundTasks.Abstractions;

/// <summary>
///     Marks an immutable message that describes background work; execution logic belongs in its handler.
/// </summary>
public interface IBackgroundTask;

/// <summary>
///     Marks background work that returns an in-process result to its caller.
/// </summary>
public interface IBackgroundTask<out TResult> : IBackgroundTask;