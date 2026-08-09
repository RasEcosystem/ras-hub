namespace RasHub.BackgroundTasks.Models;

/// <summary>
///     Lifetime timing averages accumulated independently of execution snapshot retention.
/// </summary>
public sealed record BackgroundTaskTimingStatistics(
    long SampleCount,
    TimeSpan? AverageWait,
    TimeSpan? AverageRuntime,
    TimeSpan? AverageTotalTime);