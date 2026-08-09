using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Configuration;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.BackgroundTasks.Internal.Diagnostics;
using RasHub.BackgroundTasks.Internal.Execution;
using RasHub.BackgroundTasks.Internal.Queues;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.Internal.Engine;

internal sealed class BackgroundTaskEngine : IBackgroundTaskEngine
{
    private readonly Queue<Guid> _completedHistory = new();
    private readonly object _completedHistorySync = new();

    private readonly ConcurrentDictionary<string, BackgroundTaskExecution> _deduplicated =
        new(StringComparer.Ordinal);

    private readonly BackgroundTaskEngineOptions _engineOptions;
    private readonly TimingAccumulator _interactiveTiming = new();

    private readonly ILogger<BackgroundTaskEngine> _logger;
    private readonly TimingAccumulator _maintenanceTiming = new();
    private readonly BackgroundTaskMetrics _metrics;
    private readonly TimingAccumulator _overallTiming = new();
    private readonly IBackgroundTaskQueue _queue;
    private readonly BackgroundTaskRescheduler _rescheduler;
    private readonly DateTimeOffset _startedAt;
    private readonly TimingAccumulator _synchronizationTiming = new();

    private readonly ConcurrentDictionary<Guid, BackgroundTaskExecution> _tasks =
        new();

    private readonly TimeProvider _timeProvider;
    private int _activeTasks;
    private int _completedHistoryCount;
    private long _interactiveCompletedTasks;
    private long _maintenanceCompletedTasks;
    private long _synchronizationCompletedTasks;

    public BackgroundTaskEngine(
        IBackgroundTaskQueue queue,
        BackgroundTaskRescheduler rescheduler,
        TimeProvider timeProvider,
        IOptions<BackgroundTaskEngineOptions> engineOptions,
        BackgroundTaskMetrics metrics,
        ILogger<BackgroundTaskEngine> logger)
    {
        _queue = queue;
        _rescheduler = rescheduler;
        _timeProvider = timeProvider;
        _engineOptions = engineOptions.Value;
        _metrics = metrics;
        _logger = logger;
        _startedAt = timeProvider.GetUtcNow();
    }

    public BackgroundTaskHandle Enqueue<TTask>(
        TTask task,
        BackgroundTaskOptions? options = null)
        where TTask : IBackgroundTask
    {
        ArgumentNullException.ThrowIfNull(task);

        options ??= new BackgroundTaskOptions();
        BackgroundTaskOptionsValidator.Validate(options);

        var taskType = task.GetType();
        var deduplicationKey = options.DeduplicationKey is null
            ? null
            : $"{taskType.AssemblyQualifiedName}:{options.DeduplicationKey}";

        while (true)
        {
            if (deduplicationKey is not null &&
                _deduplicated.TryGetValue(deduplicationKey, out var existing))
            {
                if (!existing.IsTerminal)
                {
                    _metrics.Deduplicated(existing);
                    return existing.CreateHandle();
                }

                _deduplicated.TryRemove(
                    new KeyValuePair<string, BackgroundTaskExecution>(
                        deduplicationKey,
                        existing));

                continue;
            }

            if (!TryReserveActiveTask())
            {
                _metrics.Rejected(taskType);
                _logger.LogWarning(
                    "Rejected background task {TaskType}: active task count reached " +
                    "its limit of {MaxActiveTasks}",
                    taskType.FullName,
                    _engineOptions.MaxActiveTasks);
                throw new BackgroundTaskRejectedException(
                    taskType,
                    "the active task count reached its configured limit");
            }

            var now = _timeProvider.GetUtcNow();
            var execution = new BackgroundTaskExecution(
                task,
                BackgroundTaskInvokerFactory.Get(taskType),
                options,
                now);

            if (deduplicationKey is not null &&
                !_deduplicated.TryAdd(deduplicationKey, execution))
            {
                ReleaseActiveTask();
                continue;
            }

            if (!_tasks.TryAdd(execution.Id, execution))
            {
                ReleaseActiveTask();
                RemoveDeduplication(deduplicationKey, execution);
                continue;
            }

            var notBefore = options.NotBefore;
            var accepted = notBefore is { } dueAt && dueAt > now;

            if (accepted)
                _rescheduler.Schedule(execution, notBefore!.Value);
            else
                accepted = _queue.TryEnqueue(execution);

            if (!accepted)
            {
                _tasks.TryRemove(execution.Id, out _);
                ReleaseActiveTask();
                RemoveDeduplication(deduplicationKey, execution);
                _metrics.Rejected(taskType);
                _logger.LogWarning(
                    "Rejected background task {TaskType} for queue {Queue}: " +
                    "queue capacity is exhausted",
                    taskType.FullName,
                    options.Queue);
                throw new BackgroundTaskRejectedException(taskType);
            }

            if (deduplicationKey is not null)
                _ = execution.Completion.ContinueWith(
                    _ => RemoveDeduplication(deduplicationKey, execution),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

            _ = execution.Completion.ContinueWith(
                _ => RecordCompletedExecution(execution),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            _metrics.Enqueued(execution);
            return execution.CreateHandle();
        }
    }

    public bool Cancel(Guid taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var execution) ||
            !execution.RequestCancellation(_timeProvider.GetUtcNow()))
            return false;

        if (execution.State == BackgroundTaskState.Canceled)
            _metrics.Canceled(execution);

        return true;
    }

    public BackgroundTaskSnapshot? GetTask(Guid taskId)
    {
        return _tasks.TryGetValue(taskId, out var execution)
            ? execution.CreateSnapshot()
            : null;
    }

    public IReadOnlyList<BackgroundTaskSnapshot> GetTasks()
    {
        return _tasks.Values
            .Select(execution => execution.CreateSnapshot())
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .ToArray();
    }

    public BackgroundTaskEngineStatistics GetStatistics()
    {
        return new BackgroundTaskEngineStatistics(
            Volatile.Read(ref _activeTasks),
            Volatile.Read(ref _completedHistoryCount),
            _queue.GetCount(BackgroundTaskQueue.Interactive),
            _queue.GetCount(BackgroundTaskQueue.Synchronization),
            _queue.GetCount(BackgroundTaskQueue.Maintenance),
            Interlocked.Read(ref _interactiveCompletedTasks),
            Interlocked.Read(ref _synchronizationCompletedTasks),
            Interlocked.Read(ref _maintenanceCompletedTasks),
            _queue.GetHighWaterMark(BackgroundTaskQueue.Interactive),
            _queue.GetHighWaterMark(BackgroundTaskQueue.Synchronization),
            _queue.GetHighWaterMark(BackgroundTaskQueue.Maintenance),
            _overallTiming.CreateSnapshot(),
            _interactiveTiming.CreateSnapshot(),
            _synchronizationTiming.CreateSnapshot(),
            _maintenanceTiming.CreateSnapshot(),
            _startedAt);
    }

    private void RecordCompletedExecution(BackgroundTaskExecution execution)
    {
        ReleaseActiveTask();
        RecordCompletedHistory(execution.Id);

        TimingAccumulator laneTiming;
        switch (execution.Options.Queue)
        {
            case BackgroundTaskQueue.Interactive:
                Interlocked.Increment(ref _interactiveCompletedTasks);
                laneTiming = _interactiveTiming;
                break;
            case BackgroundTaskQueue.Synchronization:
                Interlocked.Increment(ref _synchronizationCompletedTasks);
                laneTiming = _synchronizationTiming;
                break;
            case BackgroundTaskQueue.Maintenance:
                Interlocked.Increment(ref _maintenanceCompletedTasks);
                laneTiming = _maintenanceTiming;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(execution.Options.Queue));
        }

        var snapshot = execution.CreateSnapshot();
        if (snapshot.StartedAt is not { } startedAt ||
            snapshot.CompletedAt is not { } completedAt)
            return;

        var wait = startedAt - snapshot.CreatedAt;
        var runtime = completedAt - startedAt;
        var total = completedAt - snapshot.CreatedAt;

        laneTiming.Record(wait, runtime, total);
        _overallTiming.Record(wait, runtime, total);
    }

    private bool TryReserveActiveTask()
    {
        while (true)
        {
            var current = Volatile.Read(ref _activeTasks);
            if (current >= _engineOptions.MaxActiveTasks)
                return false;

            if (Interlocked.CompareExchange(ref _activeTasks, current + 1, current) == current)
                return true;
        }
    }

    private void ReleaseActiveTask()
    {
        Interlocked.Decrement(ref _activeTasks);
    }

    private void RecordCompletedHistory(Guid taskId)
    {
        lock (_completedHistorySync)
        {
            _completedHistory.Enqueue(taskId);
            _completedHistoryCount++;

            while (_completedHistoryCount > _engineOptions.MaxCompletedTaskHistory &&
                   _completedHistory.TryDequeue(out var expiredTaskId))
                if (_tasks.TryRemove(expiredTaskId, out _))
                    _completedHistoryCount--;
        }
    }

    internal void CancelAll()
    {
        var now = _timeProvider.GetUtcNow();

        foreach (var execution in _tasks.Values)
            if (execution.RequestCancellation(now) &&
                execution.State == BackgroundTaskState.Canceled)
                _metrics.Canceled(execution);
    }

    internal void CleanupCompletedTasks()
    {
        var cutoff = _timeProvider.GetUtcNow() - _engineOptions.CompletedTaskRetention;

        lock (_completedHistorySync)
        {
            foreach (var pair in _tasks)
            {
                var snapshot = pair.Value.CreateSnapshot();

                if (snapshot.CompletedAt is { } completedAt &&
                    completedAt <= cutoff &&
                    _tasks.TryRemove(
                        new KeyValuePair<Guid, BackgroundTaskExecution>(
                            pair.Key,
                            pair.Value)))
                    _completedHistoryCount--;
            }

            while (_completedHistory.TryPeek(out var taskId) &&
                   !_tasks.ContainsKey(taskId))
                _completedHistory.Dequeue();
        }
    }

    private void RemoveDeduplication(
        string? key,
        BackgroundTaskExecution execution)
    {
        if (key is null)
            return;

        _deduplicated.TryRemove(
            new KeyValuePair<string, BackgroundTaskExecution>(key, execution));
    }

    private sealed class TimingAccumulator
    {
        private readonly object _sync = new();
        private long _count;
        private long _runtimeTicks;
        private long _totalTicks;
        private long _waitTicks;

        public void Record(TimeSpan wait, TimeSpan runtime, TimeSpan total)
        {
            lock (_sync)
            {
                _waitTicks += wait.Ticks;
                _runtimeTicks += runtime.Ticks;
                _totalTicks += total.Ticks;
                _count++;
            }
        }

        public BackgroundTaskTimingStatistics CreateSnapshot()
        {
            lock (_sync)
            {
                if (_count == 0)
                    return new BackgroundTaskTimingStatistics(0, null, null, null);

                return new BackgroundTaskTimingStatistics(
                    _count,
                    TimeSpan.FromTicks(_waitTicks / _count),
                    TimeSpan.FromTicks(_runtimeTicks / _count),
                    TimeSpan.FromTicks(_totalTicks / _count));
            }
        }
    }
}