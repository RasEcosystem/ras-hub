namespace RasHub.Synchronization;

public interface IBackgroundTaskScheduler
{
    BackgroundTaskScheduleHandle Schedule<TTask>(
        string scheduleId,
        Func<TTask> taskFactory,
        TimeSpan interval,
        BackgroundTaskOptions? taskOptions = null,
        bool runImmediately = false)
        where TTask : IBackgroundTask;

    bool Remove(string scheduleId);

    IReadOnlyList<BackgroundTaskScheduleSnapshot> GetSchedules();
}