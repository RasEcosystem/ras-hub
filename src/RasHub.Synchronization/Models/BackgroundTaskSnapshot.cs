namespace RasHub.Synchronization;

public sealed record BackgroundTaskSnapshot(
    Guid Id,
    Type TaskType,
    BackgroundTaskState State,
    BackgroundTaskQueue Queue,
    int Priority,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? NextAttemptAt,
    bool CancellationRequested,
    string? LastError,
    string? DeduplicationKey,
    string? ConcurrencyKey);