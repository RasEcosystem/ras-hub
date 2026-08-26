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
        Exception? exception,
        object? value)
    {
        TaskId = taskId;
        Outcome = outcome;
        AttemptCount = attemptCount;
        Exception = exception;
        Value = value;
    }

    public Guid TaskId { get; }

    public BackgroundTaskOutcome Outcome { get; }

    public int AttemptCount { get; }

    public Exception? Exception { get; }

    internal object? Value { get; }

    public bool IsSucceeded =>
        Outcome == BackgroundTaskOutcome.Succeeded;

    public TResult GetValue<TResult>()
    {
        if (!IsSucceeded)
            throw new InvalidOperationException(
                "A failed or canceled background task has no result value.");

        if (Value is TResult typedValue)
            return typedValue;

        if (Value is null && default(TResult) is null)
            return default!;

        throw new InvalidOperationException(
            $"The background task result is not of type " +
            $"'{typeof(TResult).FullName}'.");
    }
}
