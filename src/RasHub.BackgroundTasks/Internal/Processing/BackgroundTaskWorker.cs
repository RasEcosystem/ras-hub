using Microsoft.Extensions.Logging;
using RasHub.BackgroundTasks.Internal.Queues;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.Internal.Processing;

/// <summary>
///     Continuously consumes one queue lane and owns keyed concurrency around each execution attempt.
/// </summary>
internal sealed class BackgroundTaskWorker
{
    private readonly BackgroundTaskAttemptRunner _attemptRunner;
    private readonly BackgroundTaskConcurrencyGate _concurrencyGate;
    private readonly ILogger<BackgroundTaskWorker> _logger;
    private readonly IBackgroundTaskQueue _queue;

    public BackgroundTaskWorker(
        IBackgroundTaskQueue queue,
        BackgroundTaskConcurrencyGate concurrencyGate,
        BackgroundTaskAttemptRunner attemptRunner,
        ILogger<BackgroundTaskWorker> logger)
    {
        _queue = queue;
        _concurrencyGate = concurrencyGate;
        _attemptRunner = attemptRunner;
        _logger = logger;
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

                    BackgroundTaskAttemptRunner.RetryPlan? retryPlan;

                    using (concurrencyLease)
                    {
                        retryPlan = await _attemptRunner.TryExecuteAsync(
                            workerId,
                            execution);
                    }

                    // The keyed lease must be released before the retry becomes
                    // visible. Otherwise another worker can dequeue the same
                    // execution while the previous attempt still owns the key
                    // and discard it as a duplicate owner entry.
                    if (retryPlan is not null)
                        _attemptRunner.PublishRetry(
                            execution,
                            retryPlan.Value);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    _attemptRunner.Cancel(execution);
                    throw;
                }
                catch (Exception exception)
                {
                    _attemptRunner.HandleInfrastructureFailure(
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
}
