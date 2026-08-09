using System.Diagnostics;
using System.Diagnostics.Metrics;
using RasHub.BackgroundTasks.Internal.Execution;

namespace RasHub.BackgroundTasks.Internal.Diagnostics;

/// <summary>
///     Publishes low-cardinality counters and attempt-duration metrics for engine activity.
/// </summary>
internal sealed class BackgroundTaskMetrics : IDisposable
{
    private readonly Histogram<double> _attemptDuration;
    private readonly Counter<long> _canceled;
    private readonly Counter<long> _deduplicated;

    private readonly Counter<long> _enqueued;
    private readonly Counter<long> _failed;

    private readonly Meter _meter = new(
        "RasHub.BackgroundTasks",
        "1.0.0");

    private readonly Counter<long> _rejected;
    private readonly Counter<long> _retried;
    private readonly Counter<long> _started;
    private readonly Counter<long> _succeeded;

    public BackgroundTaskMetrics()
    {
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
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    public void Enqueued(BackgroundTaskExecution execution)
    {
        _enqueued.Add(1, CreateTags(execution));
    }

    public void Deduplicated(BackgroundTaskExecution execution)
    {
        _deduplicated.Add(1, CreateTags(execution));
    }

    public void Rejected(Type taskType)
    {
        _rejected.Add(1, new KeyValuePair<string, object?>(
            "task.type",
            taskType.FullName));
    }

    public void Started(BackgroundTaskExecution execution)
    {
        _started.Add(1, CreateTags(execution));
    }

    public void Retried(BackgroundTaskExecution execution)
    {
        _retried.Add(1, CreateTags(execution));
    }

    public void Succeeded(BackgroundTaskExecution execution)
    {
        _succeeded.Add(1, CreateTags(execution));
    }

    public void Failed(BackgroundTaskExecution execution)
    {
        _failed.Add(1, CreateTags(execution));
    }

    public void Canceled(BackgroundTaskExecution execution)
    {
        _canceled.Add(1, CreateTags(execution));
    }

    public void RecordAttemptDuration(
        BackgroundTaskExecution execution,
        TimeSpan duration)
    {
        _attemptDuration.Record(
            duration.TotalSeconds,
            CreateTags(execution));
    }

    private static TagList CreateTags(BackgroundTaskExecution execution)
    {
        return new TagList
        {
            { "task.type", execution.BackgroundTask.GetType().FullName },
            { "queue", execution.Options.Queue.ToString() }
        };
    }
}