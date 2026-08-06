namespace RasHub.Synchronization.Models;

/// <summary>
///     Describes where, when, and under which retry and concurrency rules a task executes.
/// </summary>
public sealed record BackgroundTaskOptions
{
    /// <summary>Selects the isolated queue and worker pool used by the task.</summary>
    public BackgroundTaskQueue Queue { get; init; } =
        BackgroundTaskQueue.Synchronization;

    /// <summary>Higher values are selected first inside the chosen queue.</summary>
    public int Priority { get; init; }

    /// <summary>Maximum number of execution attempts, including the first attempt.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Delay before the first retry.</summary>
    public TimeSpan RetryDelay { get; init; } =
        TimeSpan.FromSeconds(1);

    /// <summary>Multiplier applied to the retry delay after each failed attempt.</summary>
    public double RetryBackoffFactor { get; init; } = 2;

    /// <summary>Upper bound for an exponentially increased retry delay.</summary>
    public TimeSpan MaxRetryDelay { get; init; } =
        TimeSpan.FromMinutes(1);

    /// <summary>Cooperative time limit for each attempt; null disables the limit.</summary>
    public TimeSpan? Timeout { get; init; } =
        TimeSpan.FromMinutes(5);

    /// <summary>Defers the first attempt until this UTC instant.</summary>
    public DateTimeOffset? NotBefore { get; init; }

    /// <summary>Causes equivalent active tasks of the same type to share one execution.</summary>
    public string? DeduplicationKey { get; init; }

    /// <summary>Prevents executions with the same key from running simultaneously in this process.</summary>
    public string? ConcurrencyKey { get; init; }
}