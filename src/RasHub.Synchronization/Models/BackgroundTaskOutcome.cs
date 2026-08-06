namespace RasHub.Synchronization.Models;

/// <summary>
///     Final outcome returned to callers waiting on a task handle.
/// </summary>
public enum BackgroundTaskOutcome
{
    /// <summary>The handler completed successfully.</summary>
    Succeeded = 0,

    /// <summary>The handler exhausted retries or reported a permanent failure.</summary>
    Failed = 1,

    /// <summary>Cancellation was requested before successful completion.</summary>
    Canceled = 2
}