namespace RasHub.Synchronization;

public sealed record BackgroundTaskScheduleSnapshot(
    string Id,
    Type TaskType,
    TimeSpan Interval,
    DateTimeOffset NextRunAt);