namespace RasHub.BackgroundTasks.Models;

/// <summary>
///     Terminal execution information delivered through <see cref="BackgroundTaskHandle" />.
/// </summary>
public sealed class BackgroundTaskResult
{
    internal BackgroundTaskResult(
        Guid taskId,
        BackgroundTaskOutcome outcome,
        int attemptCount,
        Exception? exception)
    {
        TaskId = taskId;
        Outcome = outcome;
        AttemptCount = attemptCount;
        Exception = exception;
    }

    public Guid TaskId { get; }

    public BackgroundTaskOutcome Outcome { get; }

    public int AttemptCount { get; }

    public Exception? Exception { get; }

    public bool IsSucceeded =>
        Outcome == BackgroundTaskOutcome.Succeeded;
}