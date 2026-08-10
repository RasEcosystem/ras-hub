using Microsoft.Extensions.Logging;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.BackgroundTasks.Internal.Diagnostics;
using RasHub.BackgroundTasks.Internal.Engine;
using RasHub.BackgroundTasks.Internal.Execution;
using RasHub.BackgroundTasks.Internal.Queues;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.Internal.Processing;

/// <summary>
///     Continuously consumes one queue lane and owns timeout, retry, cancellation, and terminal transitions.
/// </summary>
internal sealed class BackgroundTaskWorker
{
    private readonly BackgroundTaskConcurrencyGate _concurrencyGate;
    private readonly BackgroundTaskDispatcher _dispatcher;
    private readonly BackgroundTaskEngine _engine;
    private readonly ILogger<BackgroundTaskWorker> _logger;
    private readonly BackgroundTaskMetrics _metrics;

    private readonly IBackgroundTaskQueue _queue;
    private readonly BackgroundTaskRescheduler _rescheduler;
    private readonly TimeProvider _timeProvider;

    public BackgroundTaskWorker(
        IBackgroundTaskQueue queue,
        BackgroundTaskDispatcher dispatcher,
        BackgroundTaskEngine engine,
        BackgroundTaskRescheduler rescheduler,
        BackgroundTaskConcurrencyGate concurrencyGate,
        TimeProvider timeProvider,
        ILogger<BackgroundTaskWorker> logger,
        BackgroundTaskMetrics metrics)
    {
        _queue = queue;
        _dispatcher = dispatcher;
        _engine = engine;
        _rescheduler = rescheduler;
        _concurrencyGate = concurrencyGate;
        _timeProvider = timeProvider;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task RunAsync(
        int workerId,
        BackgroundTaskQueue queue,
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Background task worker {WorkerId} for queue {Queue} started",
            workerId,
            queue);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var execution = await _queue.DequeueAsync(
                    queue,
                    stoppingToken);
                IDisposable? concurrencyLease = null;

                try
                {
                    if (execution.IsTerminal)
                        continue;

                    if (!_concurrencyGate.TryAcquireOrWait(
                            execution,
                            out concurrencyLease))
                        continue;

                    RetryPlan? retryPlan;

                    using (concurrencyLease)
                    {
                        if (!_engine.TryStartExecution(
                                execution,
                                _timeProvider.GetUtcNow()))
                            continue;

                        _metrics.Started(execution);

                        retryPlan = await ExecuteAttemptAsync(
                            workerId,
                            execution);
                    }

                    // The keyed lease must be released before the retry becomes
                    // visible. Otherwise another worker can dequeue the same
                    // execution while the previous attempt still owns the key
                    // and discard it as a duplicate owner entry.
                    if (retryPlan is not null)
                        PublishRetry(execution, retryPlan.Value);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    execution.TryCancel(_timeProvider.GetUtcNow());
                    throw;
                }
                catch (Exception exception)
                {
                    HandleInfrastructureFailure(
                        execution,
                        exception,
                        stoppingToken);
                }
                finally
                {
                    // Both locals cross an await and are therefore hoisted into
                    // the worker state machine. Clear them before the next idle
                    // dequeue so one terminal payload/result is not retained
                    // indefinitely per worker.
                    concurrencyLease = null;
                    execution = null!;
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal Engine shutdown.
        }
        finally
        {
            _logger.LogInformation(
                "Background task worker {WorkerId} for queue {Queue} stopped",
                workerId,
                queue);
        }
    }

    private async Task<RetryPlan?> ExecuteAttemptAsync(
        int workerId,
        BackgroundTaskExecution execution)
    {
        var taskType = execution.BackgroundTask.GetType().FullName;
        var attemptStarted = _timeProvider.GetTimestamp();
        CancellationTokenSource? timeoutCancellation = null;

        if (execution.Options.Timeout is { } timeout)
            timeoutCancellation = new CancellationTokenSource(
                timeout,
                _timeProvider);

        using (timeoutCancellation)
        using (var attemptCancellation = CreateAttemptCancellation(
                   execution,
                   timeoutCancellation))
        {
            _logger.LogInformation(
                "Worker {WorkerId} started background task {TaskId} " +
                "of type {TaskType}, attempt {Attempt}/{MaxAttempts}",
                workerId,
                execution.Id,
                taskType,
                execution.AttemptCount,
                execution.Options.MaxAttempts);

            try
            {
                await _dispatcher.ExecuteAsync(
                    execution,
                    attemptCancellation?.Token ??
                    execution.CancellationToken);

                execution.TrySucceed(_timeProvider.GetUtcNow());

                _logger.LogInformation(
                    "Worker {WorkerId} completed background task " +
                    "{TaskId} of type {TaskType} with outcome {Outcome}",
                    workerId,
                    execution.Id,
                    taskType,
                    execution.State);

                return null;
            }
            catch (OperationCanceledException)
                when (execution.CancellationToken.IsCancellationRequested)
            {
                execution.TryCancel(_timeProvider.GetUtcNow());

                _logger.LogInformation(
                    "Background task {TaskId} of type {TaskType} was canceled",
                    execution.Id,
                    taskType);

                return null;
            }
            catch (OperationCanceledException exception)
                when (timeoutCancellation?.IsCancellationRequested == true)
            {
                return HandleFailure(
                    execution,
                    new TimeoutException(
                        $"Background task '{taskType}' exceeded " +
                        $"its timeout of {execution.Options.Timeout}.",
                        exception));
            }
            catch (Exception exception)
            {
                return HandleFailure(execution, exception);
            }
            finally
            {
                _metrics.RecordAttemptDuration(
                    execution,
                    _timeProvider.GetElapsedTime(attemptStarted));
            }
        }
    }

    private RetryPlan? HandleFailure(
        BackgroundTaskExecution execution,
        Exception exception)
    {
        var now = _timeProvider.GetUtcNow();
        var canRetry = exception is not NonRetryableBackgroundTaskException &&
                       execution.AttemptCount < execution.Options.MaxAttempts;

        if (canRetry)
        {
            var retryDelay = CalculateRetryDelay(execution);
            var nextAttemptAt = AddClamped(now, retryDelay);

            if (execution.TryScheduleRetry(exception, nextAttemptAt))
                return new RetryPlan(
                    execution.AttemptCount,
                    nextAttemptAt,
                    exception.GetType().FullName);
        }

        if (!execution.TryFail(exception, now))
            return null;

        if (execution.State == BackgroundTaskState.Canceled)
        {
            _logger.LogInformation(
                "Background task {TaskId} of type {TaskType} was canceled",
                execution.Id,
                execution.BackgroundTask.GetType().FullName);
            return null;
        }

        _logger.LogError(
            exception,
            "Background task {TaskId} failed permanently after " +
            "{AttemptCount} attempts; failure type: {FailureType}",
            execution.Id,
            execution.AttemptCount,
            exception.GetType().FullName);

        return null;
    }

    private void PublishRetry(
        BackgroundTaskExecution execution,
        RetryPlan retryPlan)
    {
        if (execution.IsTerminal)
            return;

        _rescheduler.Schedule(execution, retryPlan.NextAttemptAt);

        // Cancellation may win between the state transition and publication.
        // The terminal finalizer removes any just-published delayed entry.
        if (execution.IsTerminal)
            return;

        _metrics.Retried(execution);

        _logger.LogWarning(
            "Background task {TaskId} failed on attempt {Attempt}; " +
            "next attempt is scheduled at {NextAttemptAt}; " +
            "failure type: {FailureType}",
            execution.Id,
            retryPlan.Attempt,
            retryPlan.NextAttemptAt,
            retryPlan.FailureType);
    }

    private void HandleInfrastructureFailure(
        BackgroundTaskExecution execution,
        Exception exception,
        CancellationToken stoppingToken)
    {
        var now = _timeProvider.GetUtcNow();

        if (stoppingToken.IsCancellationRequested ||
            execution.CancellationToken.IsCancellationRequested)
            execution.TryCancel(now);
        else
            execution.TryFail(exception, now);

        _logger.LogError(
            exception,
            "Worker infrastructure failed while processing background task " +
            "{TaskId} of type {TaskType}; terminal state: {State}; " +
            "failure type: {FailureType}",
            execution.Id,
            execution.BackgroundTask.GetType().FullName,
            execution.State,
            exception.GetType().FullName);
    }

    private static CancellationTokenSource? CreateAttemptCancellation(
        BackgroundTaskExecution execution,
        CancellationTokenSource? timeoutCancellation)
    {
        return timeoutCancellation is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(
                execution.CancellationToken,
                timeoutCancellation.Token);
    }

    private static TimeSpan CalculateRetryDelay(
        BackgroundTaskExecution execution)
    {
        var exponent = Math.Max(0, execution.AttemptCount - 1);
        var multiplier = Math.Pow(
            execution.Options.RetryBackoffFactor,
            exponent);

        var calculatedTicks =
            execution.Options.RetryDelay.Ticks * multiplier;
        var maximumTicks = execution.Options.MaxRetryDelay.Ticks;
        var ticks = double.IsNaN(calculatedTicks) || calculatedTicks <= 0
            ? 0
            : calculatedTicks >= maximumTicks
                ? maximumTicks
                : (long)calculatedTicks;

        return TimeSpan.FromTicks(ticks);
    }

    private static DateTimeOffset AddClamped(
        DateTimeOffset value,
        TimeSpan delay)
    {
        var remainingTicks =
            DateTimeOffset.MaxValue.UtcTicks - value.UtcTicks;
        var delayTicks = Math.Min(delay.Ticks, remainingTicks);

        return new DateTimeOffset(
            value.UtcTicks + delayTicks,
            TimeSpan.Zero);
    }

    private readonly record struct RetryPlan(
        int Attempt,
        DateTimeOffset NextAttemptAt,
        string? FailureType);
}
