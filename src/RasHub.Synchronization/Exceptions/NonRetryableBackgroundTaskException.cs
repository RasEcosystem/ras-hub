namespace RasHub.Synchronization;

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