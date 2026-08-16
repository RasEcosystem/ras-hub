using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.IntegrationTests;

public sealed partial class BackgroundTaskEngineBehaviorTests
{
    [Fact]
    public async Task Preloaded_synchronous_lane_does_not_block_runtime_start_or_other_lanes()
    {
        using var host = CreateHost(
            services =>
            {
                services.AddSingleton<StartupProducerProbe>();
                services.AddScoped<
                    IBackgroundTaskHandler<StartupProducerTask>,
                    StartupProducerTaskHandler>();
            },
            options =>
            {
                options.InteractiveQueueCapacity = 64;
                options.InteractiveWorkerCount = 2;
            });
        var engine = GetEngine(host);
        var interactiveOptions = new BackgroundTaskOptions
        {
            Queue = BackgroundTaskQueue.Interactive,
            MaxAttempts = 1,
            Timeout = null
        };

        for (var index = 0; index < 32; index++)
            engine.Enqueue(new RecordedTask(index), interactiveOptions);

        engine.Enqueue(new StartupProducerTask(), interactiveOptions);
        var maintenance = engine.Enqueue(
            new RecordedTask(10_000),
            new BackgroundTaskOptions
            {
                Queue = BackgroundTaskQueue.Maintenance,
                MaxAttempts = 1,
                Timeout = null
            });
        var producer = host.Services.GetRequiredService<StartupProducerProbe>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var startTask = Task.Run(
            () => host.StartAsync(CancellationToken.None),
            CancellationToken.None);

        try
        {
            await startTask.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            var report = await WaitForRuntimeStatusAsync(
                host,
                "Running",
                cancellationToken);
            await producer.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            var maintenanceResult = await Await(
                maintenance,
                cancellationToken);

            Assert.Equal(
                HealthStatus.Healthy,
                report.Entries["background-tasks"].Status);
            Assert.True(maintenanceResult.IsSucceeded);
            Assert.True(producer.IsProducing);
        }
        finally
        {
            producer.StopProducing();
            await Task.WhenAll(producer.Stopped.Task, startTask)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            await host.StopAsync(cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    [Fact]
    public async Task Concurrent_start_and_stop_completes_without_faulted_runtime()
    {
        using var host = CreateHost();
        var cancellationToken = TestContext.Current.CancellationToken;
        var startTask = host.StartAsync(cancellationToken);
        var stopTask = host.StopAsync(cancellationToken);

        await Task.WhenAll(startTask, stopTask)
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        var report = await GetBackgroundTaskHealthAsync(
            host,
            cancellationToken);
        var entry = report.Entries["background-tasks"];

        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Equal("Stopped", entry.Data["runtimeStatus"]);
        Assert.Equal(0, entry.Data["liveProcessCount"]);
    }

    [Fact]
    public async Task Running_processes_are_reported_as_healthy()
    {
        using var host = CreateHost();
        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var report = await WaitForRuntimeStatusAsync(
                host,
                "Running",
                cancellationToken);
            var entry = report.Entries["background-tasks"];

            Assert.Equal(HealthStatus.Healthy, entry.Status);
            Assert.Equal("Running", entry.Data["runtimeStatus"]);
            Assert.Equal(
                entry.Data["expectedProcessCount"],
                entry.Data["liveProcessCount"]);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Stopped_runtime_is_reported_as_unhealthy()
    {
        using var host = CreateHost();
        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);
        await host.StopAsync(cancellationToken);

        var report = await GetBackgroundTaskHealthAsync(
            host,
            cancellationToken);
        var entry = report.Entries["background-tasks"];

        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Equal("Stopped", entry.Data["runtimeStatus"]);
        Assert.Equal(0, entry.Data["liveProcessCount"]);
    }

    [Fact]
    public async Task Faulted_process_stops_host_and_fails_readiness()
    {
        using var timeProvider = new CleanupFaultTimeProvider();
        using var host = CreateHost(services =>
            services.AddSingleton<TimeProvider>(timeProvider));
        var cancellationToken = TestContext.Current.CancellationToken;
        var applicationStopping = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var stoppingRegistration = host.Services
            .GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping
            .Register(applicationStopping.SetResult);

        await host.StartAsync(cancellationToken);
        await timeProvider.TimerCreationStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            cancellationToken);
        timeProvider.ReleaseFault();
        await applicationStopping.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            cancellationToken);

        var report = await GetBackgroundTaskHealthAsync(
            host,
            cancellationToken);
        var entry = report.Entries["background-tasks"];

        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Equal("Faulted", entry.Data["runtimeStatus"]);
        Assert.Equal("registry-cleanup", entry.Data["faultedProcess"]);
    }

    [Fact]
    public async Task Shutdown_waits_for_detached_cancellation_callback()
    {
        using var probe = new DetachedCancellationProbe();
        using var host = CreateHost(services =>
        {
            services.AddSingleton(probe);
            services.AddScoped<DetachedCancellationAttemptScope>();
            services.AddScoped<
                IBackgroundTaskHandler<DetachedCancellationTask>,
                DetachedCancellationTaskHandler>();
        });
        var cancellationToken = TestContext.Current.CancellationToken;
        Task? stopTask = null;

        await host.StartAsync(cancellationToken);

        try
        {
            var handle = GetEngine(host).Enqueue(
                new DetachedCancellationTask(),
                new BackgroundTaskOptions { Timeout = null });
            await probe.HandlerStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            var backgroundService = GetBackgroundService(host);
            var executeTask = backgroundService.ExecuteTask ??
                              throw new InvalidOperationException(
                                  "The background service did not start.");

            stopTask = host.StopAsync(CancellationToken.None);

            await probe.CallbackEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            await probe.ScopeDisposed.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            Assert.False(executeTask.IsCompleted);
            Assert.False(stopTask.IsCompleted);

            var stoppingReport = await GetBackgroundTaskHealthAsync(
                host,
                cancellationToken);
            Assert.Equal(
                "Stopping",
                stoppingReport.Entries["background-tasks"]
                    .Data["runtimeStatus"]);

            probe.ReleaseCallback();
            await stopTask.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            Assert.Equal(
                BackgroundTaskOutcome.Canceled,
                (await Await(handle, cancellationToken)).Outcome);
            var stoppedEntry = (await GetBackgroundTaskHealthAsync(
                    host,
                    cancellationToken))
                .Entries["background-tasks"];
            Assert.Equal("Stopped", stoppedEntry.Data["runtimeStatus"]);
            Assert.Equal(0, stoppedEntry.Data["liveProcessCount"]);
        }
        finally
        {
            probe.ReleaseCallback();

            if (stopTask is not null)
                await stopTask.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
            else
                await host.StopAsync(CancellationToken.None).WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
        }
    }

    [Fact]
    public async Task Faulted_process_joins_attempt_scopes_before_rethrowing()
    {
        using var timeProvider = new CleanupFaultTimeProvider();
        var probe = new ChildFaultJoinProbe();
        using var host = CreateHost(services =>
        {
            services.AddSingleton<TimeProvider>(timeProvider);
            services.AddSingleton(probe);
            services.AddScoped<ChildFaultAttemptScope>();
            services.AddScoped<
                IBackgroundTaskHandler<ChildFaultJoinTask>,
                ChildFaultJoinTaskHandler>();
        });
        var engine = GetEngine(host);
        var handle = engine.Enqueue(
            new ChildFaultJoinTask(),
            new BackgroundTaskOptions { Timeout = null });
        var cancellationToken = TestContext.Current.CancellationToken;
        var applicationStopping = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var stoppingRegistration = host.Services
            .GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping
            .Register(applicationStopping.SetResult);
        Task? stopTask = null;

        await host.StartAsync(cancellationToken);

        try
        {
            await Task.WhenAll(
                    timeProvider.TimerCreationStarted.Task,
                    probe.HandlerStarted.Task)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            var backgroundService = GetBackgroundService(host);
            var executeTask = backgroundService.ExecuteTask ??
                              throw new InvalidOperationException(
                                  "The background service did not start.");

            timeProvider.ReleaseFault();
            await applicationStopping.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            await probe.CancellationObserved.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            stopTask = host.StopAsync(CancellationToken.None);

            Assert.False(executeTask.IsCompleted);
            Assert.False(stopTask.IsCompleted);
            Assert.False(probe.ScopeDisposed.Task.IsCompleted);

            var faultedEntry = (await GetBackgroundTaskHealthAsync(
                    host,
                    cancellationToken))
                .Entries["background-tasks"];
            Assert.Equal(HealthStatus.Unhealthy, faultedEntry.Status);
            Assert.Equal("Faulted", faultedEntry.Data["runtimeStatus"]);
            Assert.Equal(
                "registry-cleanup",
                faultedEntry.Data["faultedProcess"]);

            probe.ReleaseHandler();
            await stopTask.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            await probe.ScopeDisposed.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await executeTask);
            Assert.Equal("Expected controlled timer failure.", exception.Message);
            Assert.Equal(
                BackgroundTaskOutcome.Canceled,
                (await Await(handle, cancellationToken)).Outcome);

            var stoppedEntry = (await GetBackgroundTaskHealthAsync(
                    host,
                    cancellationToken))
                .Entries["background-tasks"];
            Assert.Equal("Faulted", stoppedEntry.Data["runtimeStatus"]);
            Assert.Equal(0, stoppedEntry.Data["liveProcessCount"]);
        }
        finally
        {
            timeProvider.ReleaseFault();
            probe.ReleaseHandler();

            if (stopTask is not null)
                await stopTask.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
            else
                await host.StopAsync(CancellationToken.None).WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
        }
    }

    [Theory]
    [InlineData(1_025, 2, 1)]
    [InlineData(1, 1_025, 1)]
    [InlineData(1, 2, 1_025)]
    [InlineData(700, 700, 700)]
    public async Task Invalid_worker_count_fails_host_start(
        int interactiveWorkers,
        int synchronizationWorkers,
        int maintenanceWorkers)
    {
        using var host = CreateHost(configure: options =>
        {
            options.InteractiveWorkerCount = interactiveWorkers;
            options.SynchronizationWorkerCount = synchronizationWorkers;
            options.MaintenanceWorkerCount = maintenanceWorkers;
        });

        await Assert.ThrowsAsync<OptionsValidationException>(() =>
            host.StartAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(50L * TimeSpan.TicksPerDay)]
    public async Task Unsupported_cleanup_interval_fails_host_start(
        long intervalTicks)
    {
        using var host = CreateHost(configure: options =>
            options.RegistryCleanupInterval = TimeSpan.FromTicks(intervalTicks));

        await Assert.ThrowsAsync<OptionsValidationException>(() =>
            host.StartAsync(TestContext.Current.CancellationToken));
    }

    private static Task<HealthReport> GetBackgroundTaskHealthAsync(
        IHost host,
        CancellationToken cancellationToken)
    {
        return host.Services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                registration => registration.Name == "background-tasks",
                cancellationToken);
    }

    private static async Task<HealthReport> WaitForRuntimeStatusAsync(
        IHost host,
        string expectedStatus,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(5));

        while (true)
        {
            var report = await GetBackgroundTaskHealthAsync(
                host,
                timeoutCancellation.Token);
            var status = report.Entries["background-tasks"]
                .Data["runtimeStatus"];

            if (Equals(status, expectedStatus))
                return report;

            await Task.Delay(
                TimeSpan.FromMilliseconds(10),
                timeoutCancellation.Token);
        }
    }

    private static BackgroundService GetBackgroundService(IHost host)
    {
        return host.Services
            .GetServices<IHostedService>()
            .OfType<BackgroundService>()
            .Single();
    }

    private sealed class CleanupFaultTimeProvider
        : TimeProvider,
            IDisposable
    {
        private int _failNextUtcNow;
        private ControlledOneShotTimer? _timer;

        public TaskCompletionSource TimerCreationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
            Interlocked.Exchange(ref _timer, null)?.Dispose();
        }

        public void ReleaseFault()
        {
            Volatile.Read(ref _timer)?.Fire(() =>
                Volatile.Write(ref _failNextUtcNow, 1));
        }

        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Exchange(ref _failNextUtcNow, 0) == 1)
                throw new InvalidOperationException(
                    "Expected controlled timer failure.");

            return System.GetUtcNow();
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ControlledOneShotTimer(callback, state);
            var existing = Interlocked.CompareExchange(
                ref _timer,
                timer,
                null);

            if (existing is not null)
                timer.Dispose();

            TimerCreationStarted.TrySetResult();
            return existing ?? timer;
        }

        private sealed class ControlledOneShotTimer(
            TimerCallback callback,
            object? state) : ITimer
        {
            private readonly object _sync = new();
            private bool _disposed;
            private bool _fired;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_sync)
                {
                    return !_disposed;
                }
            }

            public void Dispose()
            {
                lock (_sync)
                {
                    _disposed = true;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Fire(Action beforeCallback)
            {
                lock (_sync)
                {
                    if (_disposed || _fired)
                        return;

                    _fired = true;
                    beforeCallback();
                    callback(state);
                }
            }
        }
    }

    private sealed record DetachedCancellationTask : IBackgroundTask;

    private sealed class DetachedCancellationTaskHandler(
        DetachedCancellationProbe probe,
        DetachedCancellationAttemptScope attemptScope)
        : IBackgroundTaskHandler<DetachedCancellationTask>
    {
        public async Task ExecuteAsync(
            DetachedCancellationTask task,
            CancellationToken cancellationToken)
        {
            _ = attemptScope;
            _ = cancellationToken.Register(probe.BlockCallback);
            probe.HandlerStarted.TrySetResult();

            // Deliberately return while the registered callback is still
            // blocked. The attempt scope can then finish before the detached
            // CancelAsync signal, which is the lifecycle boundary under test.
            await probe.CallbackEntered.Task;
        }
    }

    private sealed class DetachedCancellationAttemptScope(
        DetachedCancellationProbe probe) : IDisposable
    {
        public void Dispose()
        {
            probe.ScopeDisposed.TrySetResult();
        }
    }

    private sealed class DetachedCancellationProbe : IDisposable
    {
        private readonly ManualResetEventSlim _callbackRelease = new();

        public TaskCompletionSource CallbackEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource HandlerStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ScopeDisposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
            _callbackRelease.Dispose();
        }

        public void BlockCallback()
        {
            CallbackEntered.TrySetResult();
            _callbackRelease.Wait();
        }

        public void ReleaseCallback()
        {
            _callbackRelease.Set();
        }
    }

    private sealed record ChildFaultJoinTask : IBackgroundTask;

    private sealed class ChildFaultJoinTaskHandler(
        ChildFaultJoinProbe probe,
        ChildFaultAttemptScope attemptScope)
        : IBackgroundTaskHandler<ChildFaultJoinTask>
    {
        public async Task ExecuteAsync(
            ChildFaultJoinTask task,
            CancellationToken cancellationToken)
        {
            _ = attemptScope;
            using var registration = cancellationToken.Register(
                probe.MarkCancellationObserved);
            probe.HandlerStarted.TrySetResult();

            await probe.CancellationObserved.Task;
            await probe.HandlerRelease.Task;
        }
    }

    private sealed class ChildFaultAttemptScope(
        ChildFaultJoinProbe probe) : IDisposable
    {
        public void Dispose()
        {
            probe.ScopeDisposed.TrySetResult();
        }
    }

    private sealed class ChildFaultJoinProbe
    {
        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource HandlerRelease { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource HandlerStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ScopeDisposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void MarkCancellationObserved()
        {
            CancellationObserved.TrySetResult();
        }

        public void ReleaseHandler()
        {
            HandlerRelease.TrySetResult();
        }
    }

    private sealed record StartupProducerTask : IBackgroundTask;

    private sealed class StartupProducerTaskHandler(
        IBackgroundTaskEngine engine,
        StartupProducerProbe probe)
        : IBackgroundTaskHandler<StartupProducerTask>
    {
        private static readonly BackgroundTaskOptions Options = new()
        {
            Queue = BackgroundTaskQueue.Interactive,
            MaxAttempts = 1,
            Timeout = null
        };

        public Task ExecuteAsync(
            StartupProducerTask task,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            probe.MarkStarted();

            // Keep the old synchronous startup path deterministic without
            // allocating an unbounded number of executions during a failure.
            Thread.Sleep(1);

            if (probe.IsProducing)
                engine.Enqueue(task, Options);
            else
                probe.MarkStopped();

            return Task.CompletedTask;
        }
    }

    private sealed class StartupProducerProbe
    {
        private int _isProducing = 1;

        public bool IsProducing => Volatile.Read(ref _isProducing) == 1;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Stopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void MarkStarted()
        {
            Started.TrySetResult();
        }

        public void MarkStopped()
        {
            Stopped.TrySetResult();
        }

        public void StopProducing()
        {
            Volatile.Write(ref _isProducing, 0);
        }
    }
}