using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RasHub.Synchronization.Configuration;
using RasHub.Synchronization.Internal.Engine;
using RasHub.Synchronization.Internal.Queues;
using RasHub.Synchronization.Internal.Recovery;
using RasHub.Synchronization.Internal.Scheduling;
using RasHub.Synchronization.Models;

namespace RasHub.Synchronization.Internal.Processing;

/// <summary>
///     Starts all Engine loops with the host and requests cancellation of tracked work during shutdown.
/// </summary>
internal sealed class SynchronizationHostedService : BackgroundService
{
    private readonly SynchronizationEngine _engine;
    private readonly SynchronizationEngineOptions _options;
    private readonly BackgroundTaskRecoveryRunner _recoveryRunner;
    private readonly BackgroundTaskRescheduler _rescheduler;
    private readonly PeriodicBackgroundTaskScheduler _scheduler;
    private readonly TimeProvider _timeProvider;
    private readonly BackgroundTaskWorker _worker;

    public SynchronizationHostedService(
        BackgroundTaskWorker worker,
        BackgroundTaskRescheduler rescheduler,
        SynchronizationEngine engine,
        PeriodicBackgroundTaskScheduler scheduler,
        BackgroundTaskRecoveryRunner recoveryRunner,
        IOptions<SynchronizationEngineOptions> options,
        TimeProvider timeProvider)
    {
        _worker = worker;
        _rescheduler = rescheduler;
        _engine = engine;
        _scheduler = scheduler;
        _recoveryRunner = recoveryRunner;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var processes = new List<Task>
        {
            _rescheduler.RunAsync(stoppingToken),
            _scheduler.RunAsync(stoppingToken),
            RunRegistryCleanupAsync(stoppingToken)
        };

        var workerId = 0;
        AddWorkers(
            processes,
            BackgroundTaskQueue.Interactive,
            _options.InteractiveWorkerCount,
            ref workerId,
            stoppingToken);

        AddWorkers(
            processes,
            BackgroundTaskQueue.Synchronization,
            _options.WorkerCount,
            ref workerId,
            stoppingToken);

        AddWorkers(
            processes,
            BackgroundTaskQueue.Maintenance,
            _options.MaintenanceWorkerCount,
            ref workerId,
            stoppingToken);

        try
        {
            await _recoveryRunner.RunAsync(stoppingToken);
            await Task.WhenAll(processes);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal Engine shutdown.
        }
        finally
        {
            _engine.CancelAll();
        }
    }

    private void AddWorkers(
        ICollection<Task> processes,
        BackgroundTaskQueue queue,
        int count,
        ref int workerId,
        CancellationToken stoppingToken)
    {
        for (var index = 0; index < count; index++)
            processes.Add(
                _worker.RunAsync(
                    ++workerId,
                    queue,
                    stoppingToken));
    }

    private async Task RunRegistryCleanupAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            _options.RegistryCleanupInterval,
            _timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
            _engine.CleanupCompletedTasks();
    }
}