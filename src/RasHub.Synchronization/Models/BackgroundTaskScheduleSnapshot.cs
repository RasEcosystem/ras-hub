namespace RasHub.Synchronization.Models;

/// <summary>
///     Read-only view of a registered periodic schedule and its next planned run.
/// </summary>
public sealed record BackgroundTaskScheduleSnapshot(
    string Id,
    Type TaskType,
    TimeSpan Interval,
    DateTimeOffset NextRunAt);