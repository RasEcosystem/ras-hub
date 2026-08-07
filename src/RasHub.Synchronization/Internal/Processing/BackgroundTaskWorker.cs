using Microsoft.Extensions.Logging;
using RasHub.Synchronization.Exceptions;
using RasHub.Synchronization.Internal.Diagnostics;
using RasHub.Synchronization.Internal.Execution;
using RasHub.Synchronization.Internal.Queues;
using RasHub.Synchronization.Models;

namespace RasHub.Synchronization.Internal.Processing;

/// <summary>
///     Continuously consumes one queue lane and owns timeout, retry, cancellation, and terminal transitions.
/// </summary>
internal sealed class BackgroundTaskWorker
{
    private static readonly TimeSpan ConcurrencyRetryDelay =
        TimeSpan.FromMilliseconds(100);

    private readonly BackgroundTaskConcurrencyGate _concurrencyGate;
    private readonly BackgroundTaskDispatcher _dispatcher;
    private readonly ILogger<BackgroundTaskWorker> _logger;
    private readonly BackgroundTaskMetrics _metrics;

    private readonly IBackgroundTaskQueue _queue;
    private readonly BackgroundTaskRescheduler _rescheduler;
    private readonly TimeProvider _timeProvider;

    public BackgroundTaskWorker(
        IBackgroundTaskQueue queue,
        BackgroundTaskDispatcher dispatcher,
        BackgroundTaskRescheduler rescheduler,
        BackgroundTaskConcurrencyGate concurrencyGate,
        TimeProvider timeProvider,
        ILogger<BackgroundTaskWorker> logger,
        BackgroundTaskMetrics metrics)
    {
        _queue = queue;
        _dispatcher = dispatcher;
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
            "Synchronization worker {WorkerId} for queue {Queue} started",
            workerId,
            queue);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var execution = await _queue.DequeueAsync(
                    queue,
                    stoppingToken);

                if (execution.IsTerminal)
                    continue;

                if (!_concurrencyGate.TryAcquire(
                        execution.Options.ConcurrencyKey,
                        out var concurrencyLease))
                {
                    _rescheduler.Schedule(
                        execution,
                        _timeProvider.GetUtcNow() + ConcurrencyRetryDelay);

                    continue;
                }

                using (concurrencyLease)
                {
                    if (!execution.TryStart(_timeProvider.GetUtcNow()))
                        continue;

                    _metrics.Started(execution);

                    await ExecuteAttemptAsync(
                        workerId,
                        execution,
                        stoppingToken);
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
                "Synchronization worker {WorkerId} for queue {Queue} stopped",
                workerId,
                queue);
        }
    }

    private async Task ExecuteAttemptAsync(
        int workerId,
        BackgroundTaskExecution execution,
        CancellationToken stoppingToken)
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
                   stoppingToken,
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
                    attemptCancellation.Token);

                execution.TrySucceed(_timeProvider.GetUtcNow());

                if (execution.State == BackgroundTaskState.Succeeded)
                    _metrics.Succeeded(execution);
                else if (execution.State == BackgroundTaskState.Canceled)
                    _metrics.Canceled(execution);

                _logger.LogInformation(
                    "Worker {WorkerId} completed background task " +
                    "{TaskId} of type {TaskType}",
                    workerId,
                    execution.Id,
                    taskType);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested ||
                      execution.CancellationToken.IsCancellationRequested)
            {
                execution.TryCancel(_timeProvider.GetUtcNow());
                _metrics.Canceled(execution);

                _logger.LogInformation(
                    "Background task {TaskId} of type {TaskType} was canceled",
                    execution.Id,
                    taskType);
            }
            catch (OperationCanceledException exception)
                when (timeoutCancellation?.IsCancellationRequested == true)
            {
                HandleFailure(
                    execution,
                    new TimeoutException(
                        $"Background task '{taskType}' exceeded " +
                        $"its timeout of {execution.Options.Timeout}.",
                        exception));
            }
            catch (Exception exception)
            {
                HandleFailure(execution, exception);
            }
            finally
            {
                _metrics.RecordAttemptDuration(
                    execution,
                    _timeProvider.GetElapsedTime(attemptStarted));
            }
        }
    }

    private void HandleFailure(
        BackgroundTaskExecution execution,
        Exception exception)
    {
        var now = _timeProvider.GetUtcNow();
        var canRetry = exception is not NonRetryableBackgroundTaskException &&
                       execution.AttemptCount < execution.Options.MaxAttempts;

        if (canRetry)
        {
            var retryDelay = CalculateRetryDelay(execution);
            var nextAttemptAt = now + retryDelay;

            if (execution.TryScheduleRetry(exception, nextAttemptAt))
            {
                _rescheduler.Schedule(execution, nextAttemptAt);
                _metrics.Retried(execution);

                _logger.LogWarning(
                    exception,
                    "Background task {TaskId} failed on attempt {Attempt}; " +
                    "next attempt is scheduled at {NextAttemptAt}",
                    execution.Id,
                    execution.AttemptCount,
                    nextAttemptAt);

                return;
            }
        }

        if (execution.TryFail(exception, now))
            _metrics.Failed(execution);

        _logger.LogError(
            exception,
            "Background task {TaskId} failed permanently after {AttemptCount} attempts",
            execution.Id,
            execution.AttemptCount);
    }

    private static CancellationTokenSource CreateAttemptCancellation(
        BackgroundTaskExecution execution,
        CancellationToken stoppingToken,
        CancellationTokenSource? timeoutCancellation)
    {
        return timeoutCancellation is null
            ? CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
                execution.CancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
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

        var ticks = Math.Min(
            execution.Options.RetryDelay.Ticks * multiplier,
            execution.Options.MaxRetryDelay.Ticks);

        return TimeSpan.FromTicks((long)ticks);
    }
}