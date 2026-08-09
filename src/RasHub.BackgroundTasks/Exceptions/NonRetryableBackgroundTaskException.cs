namespace RasHub.BackgroundTasks.Exceptions;

/// <summary>
///     Marks a permanent handler failure that must bypass the configured retry policy.
/// </summary>
public class NonRetryableBackgroundTaskException : Exception
{
    public NonRetryableBackgroundTaskException(string message)
        : base(message)
    {
    }

    public NonRetryableBackgroundTaskException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}