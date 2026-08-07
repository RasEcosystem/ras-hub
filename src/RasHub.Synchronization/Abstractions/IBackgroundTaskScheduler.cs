using RasHub.Synchronization.Models;

namespace RasHub.Synchronization.Abstractions;

/// <summary>
///     Registers and removes in-process periodic task schedules.
/// </summary>
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