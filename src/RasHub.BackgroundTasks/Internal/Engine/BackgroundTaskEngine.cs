using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Configuration;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.BackgroundTasks.Internal.Diagnostics;
using RasHub.BackgroundTasks.Internal.Execution;
using RasHub.BackgroundTasks.Internal.Processing;
using RasHub.BackgroundTasks.Internal.Queues;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.Internal.Engine;

internal sealed class BackgroundTaskEngine :
    IBackgroundTaskEngine,
    IBackgroundTaskEngineLifecycle
{
    private readonly object _admissionSync = new();
    private readonly LinkedList<Guid> _completedHistory = [];
    private readonly Dictionary<Guid, LinkedListNode<Guid>> _completedHistoryNodes = [];
    private readonly Dictionary<Guid, BackgroundTaskSnapshot> _completedSnapshots = [];
    private readonly object _completedHistorySync = new();

    private readonly ConcurrentDictionary<long, Task> _cancellationSignals =
        new();

    private readonly ConcurrentDictionary<string, BackgroundTaskExecution> _deduplicated =
        new(StringComparer.Ordinal);

    private readonly BackgroundTaskConcurrencyGate _concurrencyGate;
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

    private readonly ConcurrentDictionary<Guid, BackgroundTaskExecution> _activeExecutions =
        new();

    private readonly TimeProvider _timeProvider;
    private bool _accepting = true;
    private int _activeTasks;
    private long _cancellationSignalSequence;
    private int _completedHistoryCount;
    private long _interactiveCompletedTasks;
    private long _maintenanceCompletedTasks;
    private long _synchronizationCompletedTasks;

    public BackgroundTaskEngine(
        IBackgroundTaskQueue queue,
        BackgroundTaskRescheduler rescheduler,
        BackgroundTaskConcurrencyGate concurrencyGate,
        TimeProvider timeProvider,
        IOptions<BackgroundTaskEngineOptions> engineOptions,
        BackgroundTaskMetrics metrics,
        ILogger<BackgroundTaskEngine> logger)
    {
        _queue = queue;
        _rescheduler = rescheduler;
        _concurrencyGate = concurrencyGate;
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

        lock (_admissionSync)
        {
            if (!_accepting)
            {
                Reject(
                    taskType,
                    "the background task engine is stopping");
            }

            while (true)
            {
                if (deduplicationKey is not null &&
                    _deduplicated.TryGetValue(
                        deduplicationKey,
                        out var existing))
                {
                    // Terminal accounting removes the registry entry before
                    // publishing Completion. Until then callers keep sharing
                    // the admitted execution instead of racing a new one.
                    if (!existing.Completion.IsCompleted)
                    {
                        _metrics.Deduplicated(existing);
                        return existing.CreateHandle();
                    }

                    RemoveDeduplication(deduplicationKey, existing);
                    continue;
                }

                if (!TryReserveActiveTask())
                {
                    Reject(
                        taskType,
                        "the active task count reached its configured limit");
                }

                BackgroundTaskExecution? execution = null;
                var reservationOwned = true;
                var deduplicationOwned = false;
                var placementMayExist = false;
                var registryEntryOwned = false;

                try
                {
                    var now = _timeProvider.GetUtcNow();
                    execution = new BackgroundTaskExecution(
                        task,
                        BackgroundTaskInvokerFactory.Get(taskType),
                        options,
                        now,
                        completed => FinalizeExecution(
                            completed,
                            deduplicationKey));

                    if (deduplicationKey is not null)
                    {
                        if (!_deduplicated.TryAdd(
                                deduplicationKey,
                                execution))
                        {
                            RollbackAdmission(
                                execution,
                                deduplicationKey,
                                ref reservationOwned,
                                ref deduplicationOwned,
                                ref placementMayExist,
                                ref registryEntryOwned);
                            continue;
                        }

                        deduplicationOwned = true;
                    }

                    var notBefore = options.NotBefore;
                    var accepted = notBefore is { } dueAt && dueAt > now;
                    placementMayExist = true;

                    if (accepted)
                        _rescheduler.Schedule(execution, notBefore!.Value);
                    else
                        accepted = _queue.TryEnqueue(execution);

                    if (!accepted)
                    {
                        RollbackAdmission(
                            execution,
                            deduplicationKey,
                            ref reservationOwned,
                            ref deduplicationOwned,
                            ref placementMayExist,
                            ref registryEntryOwned);
                        Reject(taskType, "queue capacity is exhausted");
                    }

                    // Public registry visibility is the admission commit point.
                    // Workers cannot start while this lock is held, so a
                    // successful commit makes the entire transaction visible.
                    if (!_activeExecutions.TryAdd(
                            execution.Id,
                            execution))
                    {
                        RollbackAdmission(
                            execution,
                            deduplicationKey,
                            ref reservationOwned,
                            ref deduplicationOwned,
                            ref placementMayExist,
                            ref registryEntryOwned);
                        continue;
                    }

                    registryEntryOwned = true;
                    _metrics.Enqueued(execution);
                    return execution.CreateHandle();
                }
                catch
                {
                    RollbackAdmission(
                        execution,
                        deduplicationKey,
                        ref reservationOwned,
                        ref deduplicationOwned,
                        ref placementMayExist,
                        ref registryEntryOwned);
                    throw;
                }
            }
        }
    }

    public bool Cancel(Guid taskId)
    {
        lock (_admissionSync)
        {
            if (!_activeExecutions.TryGetValue(taskId, out var execution))
                return false;

            var request = execution.PrepareCancellation(
                _timeProvider.GetUtcNow());
            if (!request.IsAccepted)
                return false;

            // Shutdown closes admission under the same lock. Tracking the
            // signal before releasing it prevents the shutdown drain from
            // overtaking a cancellation that this call has accepted.
            StartCancellationSignal(execution, request);
            return true;
        }
    }

    public BackgroundTaskSnapshot? GetTask(Guid taskId)
    {
        if (_activeExecutions.TryGetValue(taskId, out var execution))
            return execution.CreateSnapshot();

        lock (_completedHistorySync)
        {
            return _completedSnapshots.GetValueOrDefault(taskId);
        }
    }

    public IReadOnlyList<BackgroundTaskSnapshot> GetTasks()
    {
        // Read active executions first. Terminal publication adds the
        // lightweight snapshot before exact-removing the execution, so a
        // concurrent transition can overlap but cannot disappear.
        var snapshots = _activeExecutions.Values
            .Select(execution => execution.CreateSnapshot())
            .ToDictionary(snapshot => snapshot.Id);

        lock (_completedHistorySync)
        {
            foreach (var snapshot in _completedSnapshots.Values)
                snapshots[snapshot.Id] = snapshot;
        }

        return snapshots.Values
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .ToArray();
    }

    internal bool TryStartExecution(
        BackgroundTaskExecution execution,
        DateTimeOffset startedAt)
    {
        lock (_admissionSync)
        {
            if (!_activeExecutions.TryGetValue(
                    execution.Id,
                    out var activeExecution) ||
                !ReferenceEquals(activeExecution, execution))
                return false;

            if (_accepting)
                return execution.TryStart(startedAt);

            var cancellationRequest =
                execution.PrepareCancellation(startedAt);

            // StopAcceptingAndCancelAll prepares every tracked execution under
            // this lock. This fallback only protects a future lifecycle caller
            // that closes admission without first visiting this execution.
            if (cancellationRequest.IsAccepted)
                StartCancellationSignal(execution, cancellationRequest);

            return false;
        }
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

    private void FinalizeExecution(
        BackgroundTaskExecution execution,
        string? deduplicationKey)
    {
        lock (_admissionSync)
        {
            // A terminal execution must stop occupying all admission
            // structures before its completion becomes observable. Sharing
            // the admission lock also keeps Enqueued before terminal metrics
            // when a handler completes immediately.
            _queue.TryRemove(execution);
            _rescheduler.TryRemove(execution);
            _concurrencyGate.TryRemove(execution);
            RemoveDeduplication(deduplicationKey, execution);

            var snapshot = execution.CreateSnapshot();
            RecordCompletedHistory(execution, snapshot);
            ReleaseActiveTask();

            TimingAccumulator laneTiming;
            switch (snapshot.Queue)
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
                    throw new ArgumentOutOfRangeException(nameof(snapshot.Queue));
            }

            switch (snapshot.State)
            {
                case BackgroundTaskState.Succeeded:
                    _metrics.Succeeded(execution);
                    break;
                case BackgroundTaskState.Failed:
                    _metrics.Failed(execution);
                    break;
                case BackgroundTaskState.Canceled:
                    _metrics.Canceled(execution);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Execution '{execution.Id}' was finalized in state " +
                        $"'{snapshot.State}'.");
            }

            if (snapshot.StartedAt is not { } startedAt ||
                snapshot.CompletedAt is not { } completedAt)
                return;

            var wait = startedAt - snapshot.CreatedAt;
            var runtime = completedAt - startedAt;
            var total = completedAt - snapshot.CreatedAt;

            laneTiming.Record(wait, runtime, total);
            _overallTiming.Record(wait, runtime, total);
        }
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

    private void RollbackAdmission(
        BackgroundTaskExecution? execution,
        string? deduplicationKey,
        ref bool reservationOwned,
        ref bool deduplicationOwned,
        ref bool placementMayExist,
        ref bool registryEntryOwned)
    {
        try
        {
            if (registryEntryOwned && execution is not null)
            {
                _activeExecutions.TryRemove(
                    new KeyValuePair<Guid, BackgroundTaskExecution>(
                        execution.Id,
                        execution));
                registryEntryOwned = false;
            }
        }
        finally
        {
            try
            {
                if (placementMayExist && execution is not null)
                {
                    _queue.TryRemove(execution);
                    _rescheduler.TryRemove(execution);
                    placementMayExist = false;
                }
            }
            finally
            {
                try
                {
                    if (deduplicationOwned && execution is not null)
                    {
                        RemoveDeduplication(
                            deduplicationKey,
                            execution);
                        deduplicationOwned = false;
                    }
                }
                finally
                {
                    if (reservationOwned)
                    {
                        ReleaseActiveTask();
                        reservationOwned = false;
                    }
                }
            }
        }
    }

    private void RecordCompletedHistory(
        BackgroundTaskExecution execution,
        BackgroundTaskSnapshot snapshot)
    {
        lock (_completedHistorySync)
        {
            if (!_completedSnapshots.TryAdd(snapshot.Id, snapshot))
                return;

            var node = _completedHistory.AddLast(snapshot.Id);
            _completedHistoryNodes.Add(snapshot.Id, node);

            // Readers see the active execution, this completed snapshot, or
            // both transiently. GetTasks resolves the overlap by task ID.
            _activeExecutions.TryRemove(
                new KeyValuePair<Guid, BackgroundTaskExecution>(
                    execution.Id,
                    execution));

            while (_completedSnapshots.Count >
                       _engineOptions.MaxCompletedTaskHistory &&
                   _completedHistory.First is { } first)
            {
                RemoveCompletedSnapshot(first.Value);
            }

            Volatile.Write(
                ref _completedHistoryCount,
                _completedSnapshots.Count);
        }
    }

    public void StopAcceptingAndCancelAll()
    {
        var now = _timeProvider.GetUtcNow();

        lock (_admissionSync)
        {
            if (!_accepting)
                return;

            _accepting = false;
            var requests = new List<(
                BackgroundTaskExecution Execution,
                BackgroundTaskExecution.CancellationRequest Request)>();

            // Cancellation of a callback-free pending execution can finalize
            // synchronously and remove it from _activeExecutions. Snapshot and
            // prepare every execution before starting any signal so weak
            // ConcurrentDictionary enumeration cannot skip admitted work.
            foreach (var execution in _activeExecutions.Values.ToArray())
            {
                var request = execution.PrepareCancellation(now);
                if (request.IsAccepted)
                    requests.Add((execution, request));
            }

            foreach (var (execution, request) in requests)
                StartCancellationSignal(execution, request);
        }
    }

    public async Task DrainCancellationSignalsAsync()
    {
        while (true)
        {
            Task[] signals;

            lock (_admissionSync)
            {
                signals = _cancellationSignals.Values.ToArray();
            }

            if (signals.Length == 0)
                return;

            // Observer tasks contain callback failures. The loop also covers
            // signals that completed while this snapshot was being created but
            // whose exact-removal continuation has not run yet.
            await Task.WhenAll(signals).ConfigureAwait(false);
        }
    }

    internal void CleanupCompletedTasks()
    {
        var now = _timeProvider.GetUtcNow();
        var maximumRetention = now - DateTimeOffset.MinValue;
        var cutoff = _engineOptions.CompletedTaskRetention >= maximumRetention
            ? DateTimeOffset.MinValue
            : now - _engineOptions.CompletedTaskRetention;

        lock (_completedHistorySync)
        {
            var node = _completedHistory.First;
            while (node is not null)
            {
                var next = node.Next;
                var taskId = node.Value;

                if (_completedSnapshots.TryGetValue(taskId, out var snapshot) &&
                    snapshot.CompletedAt is { } completedAt &&
                    completedAt <= cutoff)
                    RemoveCompletedSnapshot(taskId);

                node = next;
            }

            Volatile.Write(
                ref _completedHistoryCount,
                _completedSnapshots.Count);
        }
    }

    private bool RemoveCompletedSnapshot(Guid taskId)
    {
        if (!_completedSnapshots.Remove(taskId))
            return false;

        if (_completedHistoryNodes.Remove(taskId, out var node))
            _completedHistory.Remove(node);

        return true;
    }

    private void StartCancellationSignal(
        BackgroundTaskExecution execution,
        BackgroundTaskExecution.CancellationRequest request)
    {
        Task signalTask;

        try
        {
            signalTask = execution.SignalCancellationAsync(request);
        }
        catch (Exception exception)
        {
            LogCancellationFailure(execution, exception);
            return;
        }

        if (!signalTask.IsCompletedSuccessfully)
        {
            var observer = Task.Run(
                () => ObserveCancellationSignalAsync(execution, signalTask),
                CancellationToken.None);
            var signalId = Interlocked.Increment(
                ref _cancellationSignalSequence);

            if (!_cancellationSignals.TryAdd(signalId, observer))
                throw new InvalidOperationException(
                    $"Cancellation signal '{signalId}' is already tracked.");

            _ = observer.ContinueWith(
                static (completedTask, state) =>
                {
                    var (engine, id) =
                        ((BackgroundTaskEngine Engine, long Id))state!;
                    engine._cancellationSignals.TryRemove(
                        new KeyValuePair<long, Task>(id, completedTask));
                },
                (this, signalId),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task ObserveCancellationSignalAsync(
        BackgroundTaskExecution execution,
        Task signalTask)
    {
        try
        {
            await signalTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogCancellationFailure(execution, exception);
        }
    }

    private void LogCancellationFailure(
        BackgroundTaskExecution execution,
        Exception exception)
    {
        try
        {
            _logger.LogError(
                "Cancellation signaling failed for background task {TaskId} " +
                "of type {TaskType}; callback failure type: {FailureType}",
                execution.Id,
                execution.BackgroundTask.GetType().FullName,
                exception.GetType().FullName);
        }
        catch (Exception)
        {
            // A logging provider is observability code and must not fault the
            // detached cancellation observer.
        }
    }

    private void Reject(Type taskType, string reason)
    {
        _metrics.Rejected(taskType);
        _logger.LogWarning(
            "Rejected background task {TaskType}: {Reason}",
            taskType.FullName,
            reason);
        throw new BackgroundTaskRejectedException(taskType, reason);
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
