namespace RasHub.Synchronization.Models;

/// <summary>
///     Internal lifecycle state exposed through task snapshots.
/// </summary>
public enum BackgroundTaskState
{
    /// <summary>The execution is queued or delayed and has not started its next attempt.</summary>
    Pending = 0,

    /// <summary>A worker is currently running the handler.</summary>
    Running = 1,

    /// <summary>The handler completed successfully.</summary>
    Succeeded = 2,

    /// <summary>The execution ended with a permanent or exhausted failure.</summary>
    Failed = 3,

    /// <summary>The execution ended after cancellation was requested.</summary>
    Canceled = 4
}