using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RasHub.BackgroundTasks.Configuration;
using RasHub.BackgroundTasks.Internal.Diagnostics;
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
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly IBackgroundTaskEngineLifecycle _lifecycle;
    private readonly BackgroundTaskEngineOptions _options;
    private readonly BackgroundTaskRescheduler _rescheduler;
    private readonly BackgroundTaskRuntimeState _runtimeState;
    private readonly PeriodicBackgroundTaskScheduler _scheduler;
    private readonly TimeProvider _timeProvider;
    private readonly BackgroundTaskWorker _worker;
    private readonly TaskCompletionSource _runtimeCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _runtimeStarted;
    private int _startRequested;
    private int _shutdownStarted;

    public BackgroundTaskHostedService(
        BackgroundTaskWorker worker,
        BackgroundTaskRescheduler rescheduler,
        BackgroundTaskEngine engine,
        IBackgroundTaskEngineLifecycle lifecycle,
        PeriodicBackgroundTaskScheduler scheduler,
        IOptions<BackgroundTaskEngineOptions> options,
        TimeProvider timeProvider,
        BackgroundTaskRuntimeState runtimeState,
        IHostApplicationLifetime hostApplicationLifetime)
    {
        _worker = worker;
        _rescheduler = rescheduler;
        _engine = engine;
        _lifecycle = lifecycle;
        _scheduler = scheduler;
        _options = options.Value;
        _timeProvider = timeProvider;
        _runtimeState = runtimeState;
        _hostApplicationLifetime = hostApplicationLifetime;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _startRequested, 1);

        try
        {
            await base.StartAsync(cancellationToken);
        }
        catch
        {
            if (Volatile.Read(ref _runtimeStarted) == 0)
                _runtimeCompleted.TrySetResult();

            throw;
        }

        if (Volatile.Read(ref _shutdownStarted) != 0)
            await WaitForRuntimeCompletionAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        InitiateShutdown();
        await base.StopAsync(cancellationToken);

        if (ExecuteTask is null)
        {
            _runtimeState.MarkStopped();
            return;
        }

        // .NET 10 can cancel the scheduled ExecuteAsync task before its
        // delegate enters, so no runtime finally block exists to publish Stopped.
        if (Volatile.Read(ref _runtimeStarted) == 0)
            await CompleteUnstartedRuntimeAsync();

        await WaitForRuntimeCompletionAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        Volatile.Write(ref _runtimeStarted, 1);
        return ObserveRuntimeCompletionAsync(RunAsync(stoppingToken));
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested ||
            !_runtimeState.TryInitialize(GetExpectedProcessCount()))
        {
            try
            {
                InitiateShutdown();
            }
            finally
            {
                await DrainCancellationSignalsAsync();
                _runtimeState.MarkStopped();
            }

            return;
        }

        using var processCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var shutdownRegistration = stoppingToken.Register(
            static state =>
                ((BackgroundTaskHostedService)state!).InitiateShutdown(),
            this);

        var processes = new List<Task>
        {
            StartProcess(
                "rescheduler",
                _rescheduler.RunAsync,
                processCancellation.Token),
            StartProcess(
                "scheduler",
                _scheduler.RunAsync,
                processCancellation.Token),
            StartProcess(
                "registry-cleanup",
                RunRegistryCleanupAsync,
                processCancellation.Token)
        };

        var workerId = 0;
        AddWorkers(
            processes,
            BackgroundTaskQueue.Interactive,
            _options.InteractiveWorkerCount,
            ref workerId,
            processCancellation.Token);

        AddWorkers(
            processes,
            BackgroundTaskQueue.Synchronization,
            _options.SynchronizationWorkerCount,
            ref workerId,
            processCancellation.Token);

        AddWorkers(
            processes,
            BackgroundTaskQueue.Maintenance,
            _options.MaintenanceWorkerCount,
            ref workerId,
            processCancellation.Token);

        _runtimeState.MarkRunning();

        var allProcesses = Task.WhenAll(processes);
        var childFaultDetected = false;

        try
        {
            var completedExecution = await Task.WhenAny(processes);

            if (!stoppingToken.IsCancellationRequested ||
                completedExecution.IsFaulted)
            {
                childFaultDetected = true;

                SignalHostStopping();
                InitiateShutdown();
                await ContainFailureAsync(processCancellation.CancelAsync());
                await ContainFailureAsync(allProcesses);

                // Await the process that made the supervisor wake up only
                // after every sibling has stopped. This preserves its original
                // exception and stack while preventing attempt scopes from
                // surviving host disposal.
                await completedExecution;

                throw new InvalidOperationException(
                    "A background task process stopped unexpectedly.");
            }

            await allProcesses;
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested &&
                  !childFaultDetected)
        {
            // Normal engine shutdown.
        }
        finally
        {
            try
            {
                InitiateShutdown();
            }
            finally
            {
                try
                {
                    await DrainCancellationSignalsAsync();
                }
                finally
                {
                    _runtimeState.MarkStopped();
                }
            }
        }
    }

    private async Task ObserveRuntimeCompletionAsync(Task runtime)
    {
        try
        {
            await runtime;
        }
        finally
        {
            _runtimeCompleted.TrySetResult();
        }
    }

    private async Task CompleteUnstartedRuntimeAsync()
    {
        try
        {
            await DrainCancellationSignalsAsync();
        }
        finally
        {
            _runtimeState.MarkStopped();
            _runtimeCompleted.TrySetResult();
        }
    }

    private Task WaitForRuntimeCompletionAsync(
        CancellationToken cancellationToken)
    {
        return Volatile.Read(ref _startRequested) == 0
            ? Task.CompletedTask
            : _runtimeCompleted.Task.WaitAsync(cancellationToken);
    }

    private void AddWorkers(
        ICollection<Task> processes,
        BackgroundTaskQueue queue,
        int count,
        ref int workerId,
        CancellationToken stoppingToken)
    {
        for (var index = 0; index < count; index++)
        {
            var currentWorkerId = ++workerId;
            processes.Add(StartProcess(
                $"worker:{currentWorkerId}:{queue}",
                cancellationToken => _worker.RunAsync(
                    currentWorkerId,
                    queue,
                    cancellationToken),
                stoppingToken));
        }
    }

    private Task StartProcess(
        string processName,
        Func<CancellationToken, Task> run,
        CancellationToken stoppingToken)
    {
        return ObserveProcessAsync(processName, run, stoppingToken);
    }

    private async Task ObserveProcessAsync(
        string processName,
        Func<CancellationToken, Task> run,
        CancellationToken stoppingToken)
    {
        _runtimeState.ProcessStarted(processName);

        try
        {
            // Every loop must return control to the supervisor before it can
            // consume work. A preloaded lane with synchronously completing
            // handlers must not prevent the remaining loops from starting.
            await Task.Yield();

            if (stoppingToken.IsCancellationRequested)
                return;

            await run(stoppingToken);

            if (!stoppingToken.IsCancellationRequested)
                throw new InvalidOperationException(
                    $"Background task process '{processName}' stopped unexpectedly.");
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal process shutdown.
        }
        catch (Exception)
        {
            _runtimeState.MarkFaulted(processName);
            throw;
        }
        finally
        {
            _runtimeState.ProcessStopped(processName);
        }
    }

    private int GetExpectedProcessCount()
    {
        return 3 +
               _options.InteractiveWorkerCount +
               _options.SynchronizationWorkerCount +
               _options.MaintenanceWorkerCount;
    }

    private void InitiateShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
            return;

        _runtimeState.MarkStopping();

        try
        {
            _scheduler.StopAcceptingAndClear();
            _lifecycle.StopAcceptingAndCancelAll();
        }
        catch (Exception)
        {
            _runtimeState.MarkFaulted("lifecycle");
            throw;
        }
    }

    private void SignalHostStopping()
    {
        try
        {
            _hostApplicationLifetime.StopApplication();
        }
        catch (Exception)
        {
            // Host-lifetime callbacks must not replace the process failure or
            // prevent sibling processes from being joined.
        }
    }

    private static async Task ContainFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception)
        {
            // The first process failure is rethrown after every sibling has
            // reached a terminal state. Awaiting here observes later faults.
        }
    }

    private async Task DrainCancellationSignalsAsync()
    {
        try
        {
            await _lifecycle.DrainCancellationSignalsAsync();
        }
        catch (Exception)
        {
            _runtimeState.MarkFaulted("cancellation-drain");
            throw;
        }
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
