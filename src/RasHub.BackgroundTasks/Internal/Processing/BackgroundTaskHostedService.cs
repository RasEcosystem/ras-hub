using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RasHub.BackgroundTasks.Configuration;
using RasHub.BackgroundTasks.Internal.Engine;
using RasHub.BackgroundTasks.Internal.Queues;
using RasHub.BackgroundTasks.Internal.Scheduling;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.Internal.Processing;

/// <summary>
///     Starts all engine loops with the host and requests cancellation of tracked work during shutdown.
/// </summary>
internal sealed class BackgroundTaskHostedService : BackgroundService
{
    private readonly BackgroundTaskEngine _engine;
    private readonly BackgroundTaskEngineOptions _options;
    private readonly BackgroundTaskRescheduler _rescheduler;
    private readonly PeriodicBackgroundTaskScheduler _scheduler;
    private readonly TimeProvider _timeProvider;
    private readonly BackgroundTaskWorker _worker;

    public BackgroundTaskHostedService(
        BackgroundTaskWorker worker,
        BackgroundTaskRescheduler rescheduler,
        BackgroundTaskEngine engine,
        PeriodicBackgroundTaskScheduler scheduler,
        IOptions<BackgroundTaskEngineOptions> options,
        TimeProvider timeProvider)
    {
        _worker = worker;
        _rescheduler = rescheduler;
        _engine = engine;
        _scheduler = scheduler;
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
            _options.SynchronizationWorkerCount,
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
            await Task.WhenAll(processes);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal engine shutdown.
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