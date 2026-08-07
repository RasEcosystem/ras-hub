using Microsoft.Extensions.Options;
using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Configuration;
using RasHub.Synchronization.Internal.Processing;
using RasHub.Synchronization.Internal.Queues;
using RasHub.Synchronization.Internal.Scheduling;
using RasHub.Synchronization.Models;

namespace RasHub.Synchronization.Internal.Diagnostics;

/// <summary>
///     Builds bounded, read-only diagnostic snapshots from the Engine's thread-safe runtime state.
/// </summary>
internal sealed class SynchronizationMonitor : ISynchronizationMonitor
{
    private const int MaximumTaskQueryLimit = 1_000;
    private const int RecentFailureLimit = 20;

    private readonly BackgroundTaskConcurrencyGate _concurrencyGate;
    private readonly ISynchronizationEngine _engine;
    private readonly BackgroundTaskRescheduler _rescheduler;
    private readonly PeriodicBackgroundTaskScheduler _scheduler;
    private readonly SynchronizationEngineSettingsSnapshot _settings;
    private readonly TimeProvider _timeProvider;

    public SynchronizationMonitor(
        ISynchronizationEngine engine,
        PeriodicBackgroundTaskScheduler scheduler,
        BackgroundTaskRescheduler rescheduler,
        BackgroundTaskConcurrencyGate concurrencyGate,
        TimeProvider timeProvider,
        IOptions<SynchronizationEngineOptions> options)
    {
        _engine = engine;
        _scheduler = scheduler;
        _rescheduler = rescheduler;
        _concurrencyGate = concurrencyGate;
        _timeProvider = timeProvider;

        var value = options.Value;
        _settings = new SynchronizationEngineSettingsSnapshot(
            value.MaxTrackedTasks,
            value.InteractiveQueueCapacity,
            value.QueueCapacity,
            value.MaintenanceQueueCapacity,
            value.InteractiveWorkerCount,
            value.WorkerCount,
            value.MaintenanceWorkerCount);
    }

    public SynchronizationMonitorSnapshot GetSnapshot()
    {
        var tasks = _engine.GetTasks();
        var tasksByState = Enum
            .GetValues<BackgroundTaskState>()
            .ToDictionary(
                state => state,
                state => tasks.Count(task => task.State == state));

        var runningTasks = tasks
            .Where(task => task.State == BackgroundTaskState.Running)
            .OrderBy(task => task.StartedAt)
            .ToArray();

        var recentFailures = tasks
            .Where(task => task.State == BackgroundTaskState.Failed)
            .OrderByDescending(task => task.CompletedAt)
            .Take(RecentFailureLimit)
            .ToArray();

        return new SynchronizationMonitorSnapshot(
            _timeProvider.GetUtcNow(),
            _settings,
            _engine.GetStatistics(),
            tasksByState,
            _rescheduler.DelayedExecutionCount,
            _concurrencyGate.ActiveKeyCount,
            _scheduler.GetSchedules(),
            runningTasks,
            recentFailures);
    }

    public BackgroundTaskSnapshot? GetTask(Guid taskId)
    {
        return _engine.GetTask(taskId);
    }

    public IReadOnlyList<BackgroundTaskSnapshot> GetTasks(
        BackgroundTaskState? state = null,
        BackgroundTaskQueue? queue = null,
        int limit = 100)
    {
        if (limit is < 1 or > MaximumTaskQueryLimit)
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"Task query limit must be between 1 and {MaximumTaskQueryLimit}.");

        return _engine.GetTasks()
            .Where(task => state is null || task.State == state)
            .Where(task => queue is null || task.Queue == queue)
            .Take(limit)
            .ToArray();
    }

    public IReadOnlyList<BackgroundTaskScheduleSnapshot> GetSchedules()
    {
        return _scheduler.GetSchedules();
    }
}