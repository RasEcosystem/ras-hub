namespace RasHub.BackgroundTasks.Models;

/// <summary>
///     Thread-safe read-only view of an execution's current state and policy.
/// </summary>
public sealed record BackgroundTaskSnapshot(
    Guid Id,
    Type TaskType,
    BackgroundTaskState State,
    BackgroundTaskQueue Queue,
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