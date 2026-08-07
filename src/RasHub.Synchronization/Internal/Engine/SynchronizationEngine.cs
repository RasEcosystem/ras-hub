using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Configuration;
using RasHub.Synchronization.Exceptions;
using RasHub.Synchronization.Internal.Diagnostics;
using RasHub.Synchronization.Internal.Execution;
using RasHub.Synchronization.Internal.Queues;
using RasHub.Synchronization.Models;

namespace RasHub.Synchronization.Internal.Engine;

internal sealed class SynchronizationEngine : ISynchronizationEngine
{
    private readonly ConcurrentDictionary<string, BackgroundTaskExecution> _deduplicated =
        new(StringComparer.Ordinal);

    private readonly ILogger<SynchronizationEngine> _logger;
    private readonly BackgroundTaskMetrics _metrics;
    private readonly IBackgroundTaskQueue _queue;
    private readonly SynchronizationEngineOptions _engineOptions;
    private readonly BackgroundTaskRescheduler _rescheduler;
    private readonly DateTimeOffset _startedAt;

    private readonly ConcurrentDictionary<Guid, BackgroundTaskExecution> _tasks =
        new();

    private readonly TimeProvider _timeProvider;
    private readonly TimingAccumulator _overallTiming = new();
    private readonly TimingAccumulator _interactiveTiming = new();
    private readonly TimingAccumulator _synchronizationTiming = new();
    private readonly TimingAccumulator _maintenanceTiming = new();
    private long _interactiveCompletedTasks;
    private long _maintenanceCompletedTasks;
    private long _synchronizationCompletedTasks;

    public SynchronizationEngine(
        IBackgroundTaskQueue queue,
        BackgroundTaskRescheduler rescheduler,
        TimeProvider timeProvider,
        IOptions<SynchronizationEngineOptions> engineOptions,
        BackgroundTaskMetrics metrics,
        ILogger<SynchronizationEngine> logger)
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

            if (_tasks.Count >= _engineOptions.MaxTrackedTasks)
            {
                _metrics.Rejected(taskType);
                _logger.LogWarning(
                    "Rejected background task {TaskType}: task registry reached " +
                    "its limit of {MaxTrackedTasks}",
                    taskType.FullName,
                    _engineOptions.MaxTrackedTasks);
                throw new BackgroundTaskRejectedException(
                    taskType,
                    "the task registry reached its configured limit");
            }

            var now = _timeProvider.GetUtcNow();
            var execution = new BackgroundTaskExecution(
                task,
                BackgroundTaskInvokerFactory.Get(taskType),
                options,
                now);

            if (deduplicationKey is not null &&
                !_deduplicated.TryAdd(deduplicationKey, execution))
                continue;

            if (!_tasks.TryAdd(execution.Id, execution))
            {
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

    public SynchronizationEngineStatistics GetStatistics()
    {
        return new SynchronizationEngineStatistics(
            _tasks.Count,
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

        foreach (var pair in _tasks)
        {
            var snapshot = pair.Value.CreateSnapshot();

            if (snapshot.CompletedAt is { } completedAt &&
                completedAt <= cutoff)
                _tasks.TryRemove(
                    new KeyValuePair<Guid, BackgroundTaskExecution>(
                        pair.Key,
                        pair.Value));
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
}
