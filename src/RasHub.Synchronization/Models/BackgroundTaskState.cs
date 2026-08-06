namespace RasHub.Synchronization;

public enum BackgroundTaskState
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Canceled = 4
}