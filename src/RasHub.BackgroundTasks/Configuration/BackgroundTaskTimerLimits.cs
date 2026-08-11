namespace RasHub.BackgroundTasks.Configuration;

/// <summary>Timer ranges supported by the default .NET time provider.</summary>
public static class BackgroundTaskTimerLimits
{
    /// <summary>Shortest interval supported by <see cref="PeriodicTimer" />.</summary>
    public static readonly TimeSpan MinimumPeriodicInterval =
        TimeSpan.FromMilliseconds(1);

    /// <summary>Longest duration supported by default .NET timers.</summary>
    public static readonly TimeSpan MaximumTimerDuration =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1L);
}