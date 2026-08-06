namespace RasHub.Synchronization;

public sealed record BackgroundTaskOptions
{
    public BackgroundTaskQueue Queue { get; init; } =
        BackgroundTaskQueue.Synchronization;

    public int Priority { get; init; }

    public int MaxAttempts { get; init; } = 3;

    public TimeSpan RetryDelay { get; init; } =
        TimeSpan.FromSeconds(1);

    public double RetryBackoffFactor { get; init; } = 2;

    public TimeSpan MaxRetryDelay { get; init; } =
        TimeSpan.FromMinutes(1);

    public TimeSpan? Timeout { get; init; } =
        TimeSpan.FromMinutes(5);

    public DateTimeOffset? NotBefore { get; init; }

    public string? DeduplicationKey { get; init; }

    public string? ConcurrencyKey { get; init; }
}