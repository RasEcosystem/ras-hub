using System.Diagnostics;
using System.Diagnostics.Metrics;
using RasHub.BackgroundTasks.Internal.Execution;
using RasHub.BackgroundTasks.Internal.Processing;
using RasHub.BackgroundTasks.Internal.Queues;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.Internal.Diagnostics;

/// <summary>
///     Publishes low-cardinality counters and attempt-duration metrics for engine activity.
/// </summary>
internal sealed class BackgroundTaskMetrics : IDisposable
{
    private readonly UpDownCounter<long> _active;
    private readonly Histogram<double> _attemptDuration;
    private readonly Counter<long> _canceled;
    private readonly BackgroundTaskConcurrencyGate _concurrencyGate;
    private readonly Counter<long> _deduplicated;

    private readonly Counter<long> _enqueued;
    private readonly Counter<long> _failed;

    private readonly Meter _meter = new(
        BackgroundTaskTelemetry.MeterName,
        "1.0.0");

    private readonly IBackgroundTaskQueue _queue;

    private readonly Counter<long> _rejected;
    private readonly BackgroundTaskRescheduler _rescheduler;
    private readonly Counter<long> _retried;
    private readonly BackgroundTaskRuntimeState _runtimeState;
    private readonly Counter<long> _started;
    private readonly Counter<long> _succeeded;
    private readonly TimeProvider _timeProvider;

    public BackgroundTaskMetrics(
        IBackgroundTaskQueue queue,
        BackgroundTaskRescheduler rescheduler,
        BackgroundTaskConcurrencyGate concurrencyGate,
        BackgroundTaskRuntimeState runtimeState,
        TimeProvider timeProvider)
    {
        _queue = queue;
        _rescheduler = rescheduler;
        _concurrencyGate = concurrencyGate;
        _runtimeState = runtimeState;
        _timeProvider = timeProvider;

        _enqueued = _meter.CreateCounter<long>(
            "rashub.background_tasks.enqueued");
        _deduplicated = _meter.CreateCounter<long>(
            "rashub.background_tasks.deduplicated");
        _rejected = _meter.CreateCounter<long>(
            "rashub.background_tasks.rejected");
        _started = _meter.CreateCounter<long>(
            "rashub.background_tasks.started");
        _retried = _meter.CreateCounter<long>(
            "rashub.background_tasks.retried");
        _succeeded = _meter.CreateCounter<long>(
            "rashub.background_tasks.succeeded");
        _failed = _meter.CreateCounter<long>(
            "rashub.background_tasks.failed");
        _canceled = _meter.CreateCounter<long>(
            "rashub.background_tasks.canceled");
        _attemptDuration = _meter.CreateHistogram<double>(
            "rashub.background_tasks.attempt.duration",
            "s");
        _active = _meter.CreateUpDownCounter<long>(
            "rashub.background_tasks.active",
            "{execution}");
        _meter.CreateObservableGauge(
            "rashub.background_tasks.queue.length",
            ObserveQueueLengths,
            "{task}");
        _meter.CreateObservableGauge(
            "rashub.background_tasks.queue.oldest.age",
            ObserveOldestQueueAges,
            "s");
        _meter.CreateObservableGauge(
            "rashub.background_tasks.delayed",
            () => _rescheduler.DelayedExecutionCount,
            "{execution}");
        _meter.CreateObservableGauge(
            "rashub.background_tasks.delayed.overdue",
            () => _rescheduler.GetOverdueExecutionCount(
                _timeProvider.GetUtcNow()),
            "{execution}");
        _meter.CreateObservableGauge(
            "rashub.background_tasks.concurrency.keys.active",
            () => _concurrencyGate.ActiveKeyCount,
            "{key}");
        _meter.CreateObservableGauge(
            "rashub.background_tasks.concurrency.waiters",
            () => _concurrencyGate.WaitingExecutionCount,
            "{execution}");
        _meter.CreateObservableGauge(
            "rashub.background_tasks.processes.live",
            () => _runtimeState.CreateSnapshot().LiveProcessCount,
            "{process}");
        _meter.CreateObservableGauge(
            "rashub.background_tasks.processes.expected",
            () => _runtimeState.CreateSnapshot().ExpectedProcessCount,
            "{process}");
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    public void Enqueued(BackgroundTaskExecution execution)
    {
        // Publish the active balance first. MeterListener callbacks execute
        // synchronously and may re-enter the engine (for example, by
        // canceling this execution from the enqueued measurement). Recording
        // the balance first keeps that terminal -1 ordered after this +1.
        RecordSafely(() => _active.Add(1, CreateTags(execution)));
        RecordSafely(() => _enqueued.Add(1, CreateTags(execution)));
    }

    public void Deduplicated(BackgroundTaskExecution execution)
    {
        RecordSafely(() => _deduplicated.Add(1, CreateTags(execution)));
    }

    public void Rejected(Type taskType)
    {
        RecordSafely(() => _rejected.Add(
            1,
            new KeyValuePair<string, object?>(
                "task.type",
                taskType.FullName)));
    }

    public void Started(BackgroundTaskExecution execution)
    {
        RecordSafely(() => _started.Add(1, CreateTags(execution)));
    }

    public void Retried(BackgroundTaskExecution execution)
    {
        RecordSafely(() => _retried.Add(1, CreateTags(execution)));
    }

    public void Succeeded(BackgroundTaskExecution execution)
    {
        RecordSafely(() => _succeeded.Add(1, CreateTags(execution)));
        RecordSafely(() => _active.Add(-1, CreateTags(execution)));
    }

    public void Failed(BackgroundTaskExecution execution)
    {
        RecordSafely(() => _failed.Add(1, CreateTags(execution)));
        RecordSafely(() => _active.Add(-1, CreateTags(execution)));
    }

    public void Canceled(BackgroundTaskExecution execution)
    {
        RecordSafely(() => _canceled.Add(1, CreateTags(execution)));
        RecordSafely(() => _active.Add(-1, CreateTags(execution)));
    }

    public void RecordAttemptDuration(
        BackgroundTaskExecution execution,
        TimeSpan duration)
    {
        RecordSafely(() => _attemptDuration.Record(
            duration.TotalSeconds,
            CreateTags(execution)));
    }

    private static void RecordSafely(Action record)
    {
        try
        {
            record();
        }
        catch (Exception)
        {
            // MeterListener callbacks execute synchronously on the producer's
            // thread. Observability must never reject admitted work, change an
            // execution outcome, or stop a worker because a listener failed.
        }
    }

    private static TagList CreateTags(BackgroundTaskExecution execution)
    {
        return new TagList
        {
            { "task.type", execution.BackgroundTask.GetType().FullName },
            { "queue", execution.Options.Queue.ToString() }
        };
    }

    private IEnumerable<Measurement<int>> ObserveQueueLengths()
    {
        yield return CreateQueueMeasurement(BackgroundTaskQueue.Interactive);
        yield return CreateQueueMeasurement(BackgroundTaskQueue.Synchronization);
        yield return CreateQueueMeasurement(BackgroundTaskQueue.Maintenance);
    }

    private Measurement<int> CreateQueueMeasurement(BackgroundTaskQueue queue)
    {
        return new Measurement<int>(
            _queue.GetCount(queue),
            new KeyValuePair<string, object?>("queue", queue.ToString()));
    }

    private IEnumerable<Measurement<double>> ObserveOldestQueueAges()
    {
        var now = _timeProvider.GetUtcNow();

        yield return CreateQueueAgeMeasurement(
            BackgroundTaskQueue.Interactive,
            now);
        yield return CreateQueueAgeMeasurement(
            BackgroundTaskQueue.Synchronization,
            now);
        yield return CreateQueueAgeMeasurement(
            BackgroundTaskQueue.Maintenance,
            now);
    }

    private Measurement<double> CreateQueueAgeMeasurement(
        BackgroundTaskQueue queue,
        DateTimeOffset now)
    {
        var enqueuedAt = _queue.GetOldestEnqueuedAt(queue);
        var age = enqueuedAt is null || enqueuedAt >= now
            ? 0
            : (now - enqueuedAt.Value).TotalSeconds;

        return new Measurement<double>(
            age,
            new KeyValuePair<string, object?>("queue", queue.ToString()));
    }
}
