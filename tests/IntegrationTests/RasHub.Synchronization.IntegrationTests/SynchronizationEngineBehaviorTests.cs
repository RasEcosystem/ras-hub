using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Configuration;
using RasHub.Synchronization.Exceptions;
using RasHub.Synchronization.Models;

namespace RasHub.Synchronization.IntegrationTests;

public sealed class SynchronizationEngineBehaviorTests
{
    [Fact]
    public async Task Failed_task_returns_failure_without_stopping_workers()
    {
        using var host = CreateHost(services =>
            services.AddScoped<
                IBackgroundTaskHandler<FailingTask>,
                FailingTaskHandler>());

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var failed = engine.Enqueue(
                new FailingTask(),
                new BackgroundTaskOptions { MaxAttempts = 1 });

            var failedResult = await Await(failed, cancellationToken);

            Assert.Equal(BackgroundTaskOutcome.Failed, failedResult.Outcome);
            Assert.IsType<InvalidOperationException>(failedResult.Exception);

            var successful = engine.Enqueue(new RecordedTask(42));
            var successfulResult = await Await(successful, cancellationToken);

            Assert.True(successfulResult.IsSucceeded);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Retry_policy_repeats_task_until_it_succeeds()
    {
        using var host = CreateHost(services =>
        {
            services.AddSingleton<AttemptProbe>();
            services.AddScoped<
                IBackgroundTaskHandler<RetryTask>,
                RetryTaskHandler>();
        });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var handle = GetEngine(host).Enqueue(
                new RetryTask(2),
                new BackgroundTaskOptions
                {
                    MaxAttempts = 3,
                    RetryDelay = TimeSpan.Zero
                });

            var result = await Await(handle, cancellationToken);

            Assert.True(result.IsSucceeded);
            Assert.Equal(3, result.AttemptCount);
            Assert.Equal(
                3,
                host.Services.GetRequiredService<AttemptProbe>().Attempts);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Running_task_can_be_canceled()
    {
        using var host = CreateHost(services =>
        {
            services.AddSingleton<BlockingProbe>();
            services.AddScoped<
                IBackgroundTaskHandler<BlockingTask>,
                BlockingTaskHandler>();
        });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var handle = engine.Enqueue(new BlockingTask());
            var probe = host.Services.GetRequiredService<BlockingProbe>();

            await probe.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            Assert.True(engine.Cancel(handle.Id));

            var result = await Await(handle, cancellationToken);

            Assert.Equal(BackgroundTaskOutcome.Canceled, result.Outcome);
            Assert.Equal(
                BackgroundTaskState.Canceled,
                engine.GetTask(handle.Id)?.State);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Attempt_timeout_is_reported_as_failure()
    {
        using var host = CreateHost(services =>
        {
            services.AddSingleton<BlockingProbe>();
            services.AddScoped<
                IBackgroundTaskHandler<BlockingTask>,
                BlockingTaskHandler>();
        });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var handle = GetEngine(host).Enqueue(
                new BlockingTask(),
                new BackgroundTaskOptions
                {
                    MaxAttempts = 1,
                    Timeout = TimeSpan.FromMilliseconds(50)
                });

            var result = await Await(handle, cancellationToken);

            Assert.Equal(BackgroundTaskOutcome.Failed, result.Outcome);
            Assert.IsType<TimeoutException>(result.Exception);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Duplicate_active_tasks_share_the_same_execution()
    {
        using var host = CreateHost(services =>
        {
            services.AddSingleton<BlockingProbe>();
            services.AddScoped<
                IBackgroundTaskHandler<BlockingTask>,
                BlockingTaskHandler>();
        });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var options = new BackgroundTaskOptions
            {
                DeduplicationKey = "same-gate"
            };

            var first = engine.Enqueue(new BlockingTask(), options);
            var second = engine.Enqueue(new BlockingTask(), options);

            Assert.Equal(first.Id, second.Id);

            var probe = host.Services.GetRequiredService<BlockingProbe>();
            probe.Release.TrySetResult();

            var results = await Task.WhenAll(
                Await(first, cancellationToken),
                Await(second, cancellationToken));

            Assert.All(results, result => Assert.True(result.IsSucceeded));
            Assert.Equal(1, probe.InvocationCount);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Concurrency_key_serializes_related_tasks()
    {
        using var host = CreateHost(
            services =>
            {
                services.AddSingleton<ConcurrencyProbe>();
                services.AddScoped<
                    IBackgroundTaskHandler<ConcurrentTask>,
                    ConcurrentTaskHandler>();
            },
            options => options.WorkerCount = 4);

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var options = new BackgroundTaskOptions
            {
                ConcurrencyKey = "gate:42"
            };

            var handles = Enumerable.Range(0, 4)
                .Select(index => engine.Enqueue(new ConcurrentTask(index), options))
                .ToArray();

            var results = await Task.WhenAll(
                handles.Select(handle => Await(handle, cancellationToken)));

            Assert.All(results, result => Assert.True(result.IsSucceeded));
            Assert.Equal(
                1,
                host.Services.GetRequiredService<ConcurrencyProbe>()
                    .MaximumConcurrency);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Higher_priority_task_is_dequeued_first()
    {
        using var host = CreateHost();
        var engine = GetEngine(host);

        var low = engine.Enqueue(
            new RecordedTask(1),
            new BackgroundTaskOptions { Priority = -100 });

        var high = engine.Enqueue(
            new RecordedTask(2),
            new BackgroundTaskOptions { Priority = 100 });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            await Task.WhenAll(
                Await(low, cancellationToken),
                Await(high, cancellationToken));

            var values = host.Services
                .GetRequiredService<RecordingProbe>()
                .Values;

            Assert.Equal([2, 1], values);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Immediate_periodic_schedule_enqueues_task()
    {
        using var host = CreateHost();
        var scheduler = host.Services
            .GetRequiredService<IBackgroundTaskScheduler>();

        using var schedule = scheduler.Schedule(
            "test-schedule",
            () => new RecordedTask(7),
            TimeSpan.FromHours(1),
            runImmediately: true);

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var probe = host.Services.GetRequiredService<RecordingProbe>();

            await probe.Recorded.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            Assert.Contains(7, probe.Values);
            Assert.Single(scheduler.GetSchedules());
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Recovery_source_can_restore_work_when_host_starts()
    {
        using var host = CreateHost(services =>
            services.AddScoped<
                IBackgroundTaskRecoverySource,
                TestRecoverySource>());

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var probe = host.Services.GetRequiredService<RecordingProbe>();

            await probe.Recorded.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            Assert.Contains(99, probe.Values);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public void Full_queue_rejects_new_task_without_returning_handle()
    {
        using var host = CreateHost(
            configure: options => options.QueueCapacity = 1);

        var engine = GetEngine(host);
        engine.Enqueue(new RecordedTask(1));

        Assert.Throws<BackgroundTaskRejectedException>(() =>
            engine.Enqueue(new RecordedTask(2)));
    }

    [Fact]
    public async Task Pending_task_can_be_canceled_before_host_starts()
    {
        using var host = CreateHost();
        var engine = GetEngine(host);
        var handle = engine.Enqueue(new RecordedTask(1));

        Assert.True(engine.Cancel(handle.Id));

        var result = await Await(
            handle,
            TestContext.Current.CancellationToken);

        Assert.Equal(BackgroundTaskOutcome.Canceled, result.Outcome);
        Assert.Empty(
            host.Services.GetRequiredService<RecordingProbe>().Values);
    }

    [Fact]
    public async Task Missing_handler_is_a_non_retryable_failure()
    {
        using var host = CreateHost();
        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var handle = GetEngine(host).Enqueue(
                new UnregisteredTask(),
                new BackgroundTaskOptions
                {
                    MaxAttempts = 10,
                    RetryDelay = TimeSpan.Zero
                });

            var result = await Await(handle, cancellationToken);

            Assert.Equal(BackgroundTaskOutcome.Failed, result.Outcome);
            Assert.Equal(1, result.AttemptCount);
            Assert.IsType<NonRetryableBackgroundTaskException>(
                result.Exception);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Interactive_queue_is_not_blocked_by_maintenance_work()
    {
        using var host = CreateHost(services =>
        {
            services.AddSingleton<BlockingProbe>();
            services.AddScoped<
                IBackgroundTaskHandler<BlockingTask>,
                BlockingTaskHandler>();
        });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var maintenance = engine.Enqueue(
                new BlockingTask(),
                new BackgroundTaskOptions
                {
                    Queue = BackgroundTaskQueue.Maintenance
                });

            var blockingProbe = host.Services
                .GetRequiredService<BlockingProbe>();

            await blockingProbe.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            var interactive = engine.Enqueue(
                new RecordedTask(123),
                new BackgroundTaskOptions
                {
                    Queue = BackgroundTaskQueue.Interactive
                });

            var interactiveResult = await Await(
                interactive,
                cancellationToken);

            Assert.True(interactiveResult.IsSucceeded);

            blockingProbe.Release.TrySetResult();
            Assert.True((await Await(maintenance, cancellationToken)).IsSucceeded);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Host_shutdown_cancels_running_and_pending_tasks()
    {
        using var host = CreateHost(
            services =>
            {
                services.AddSingleton<BlockingProbe>();
                services.AddScoped<
                    IBackgroundTaskHandler<BlockingTask>,
                    BlockingTaskHandler>();
            },
            options => options.WorkerCount = 1);

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        var engine = GetEngine(host);
        var running = engine.Enqueue(new BlockingTask());
        var pending = engine.Enqueue(new RecordedTask(1));

        await host.Services
            .GetRequiredService<BlockingProbe>()
            .Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

        await host.StopAsync(cancellationToken);

        Assert.Equal(
            BackgroundTaskOutcome.Canceled,
            (await Await(running, cancellationToken)).Outcome);

        Assert.Equal(
            BackgroundTaskOutcome.Canceled,
            (await Await(pending, cancellationToken)).Outcome);
    }

    [Fact]
    public async Task Readiness_is_unhealthy_when_queue_capacity_is_exhausted()
    {
        using var host = CreateHost(
            configure: options => options.QueueCapacity = 1);

        GetEngine(host).Enqueue(new RecordedTask(1));

        var report = await host.Services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                registration => registration.Name == "synchronization",
                TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
    }

    [Fact]
    public async Task Lifetime_timing_statistics_survive_execution_cleanup()
    {
        using var host = CreateHost(configure: options =>
        {
            options.CompletedTaskRetention = TimeSpan.Zero;
            options.RegistryCleanupInterval = TimeSpan.FromMilliseconds(10);
        });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var handle = engine.Enqueue(new RecordedTask(1));
            await Await(handle, cancellationToken);

            SynchronizationEngineStatistics statistics;
            do
            {
                statistics = engine.GetStatistics();
                if (statistics.OverallTiming.SampleCount == 0)
                    await Task.Delay(10, cancellationToken);
            } while (statistics.OverallTiming.SampleCount == 0);

            var timingBeforeCleanup = statistics.OverallTiming;

            while (engine.GetTask(handle.Id) is not null)
                await Task.Delay(10, cancellationToken);

            var timingAfterCleanup = engine.GetStatistics().OverallTiming;

            Assert.Equal(1, timingBeforeCleanup.SampleCount);
            Assert.Equal(timingBeforeCleanup, timingAfterCleanup);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    private static IHost CreateHost(
        Action<IServiceCollection>? register = null,
        Action<SynchronizationEngineOptions>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton<RecordingProbe>();
        builder.Services.AddScoped<
            IBackgroundTaskHandler<RecordedTask>,
            RecordedTaskHandler>();

        register?.Invoke(builder.Services);

        builder.Services.AddRasHubSynchronization(options =>
        {
            options.QueueCapacity = 32;
            options.InteractiveQueueCapacity = 8;
            options.MaintenanceQueueCapacity = 8;
            options.WorkerCount = 2;
            options.InteractiveWorkerCount = 1;
            options.MaintenanceWorkerCount = 1;
            configure?.Invoke(options);
        });

        return builder.Build();
    }

    private static ISynchronizationEngine GetEngine(IHost host)
    {
        return host.Services.GetRequiredService<ISynchronizationEngine>();
    }

    private static Task<BackgroundTaskResult> Await(
        BackgroundTaskHandle handle,
        CancellationToken cancellationToken)
    {
        return handle
            .WaitAsync(cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }
}

internal sealed record RecordedTask(int Value) : IBackgroundTask;

internal sealed class RecordedTaskHandler(RecordingProbe probe)
    : IBackgroundTaskHandler<RecordedTask>
{
    public Task ExecuteAsync(
        RecordedTask task,
        CancellationToken cancellationToken)
    {
        probe.Record(task.Value);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingProbe
{
    private readonly ConcurrentQueue<int> _values = new();

    public TaskCompletionSource Recorded { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int[] Values => _values.ToArray();

    public void Record(int value)
    {
        _values.Enqueue(value);
        Recorded.TrySetResult();
    }
}

internal sealed record FailingTask : IBackgroundTask;

internal sealed record UnregisteredTask : IBackgroundTask;

internal sealed class FailingTaskHandler
    : IBackgroundTaskHandler<FailingTask>
{
    public Task ExecuteAsync(
        FailingTask task,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Expected failure.");
    }
}

internal sealed record RetryTask(int FailuresBeforeSuccess) : IBackgroundTask;

internal sealed class RetryTaskHandler(AttemptProbe probe)
    : IBackgroundTaskHandler<RetryTask>
{
    public Task ExecuteAsync(
        RetryTask task,
        CancellationToken cancellationToken)
    {
        if (probe.Increment() <= task.FailuresBeforeSuccess)
            throw new InvalidOperationException("Transient failure.");

        return Task.CompletedTask;
    }
}

internal sealed class AttemptProbe
{
    private int _attempts;
    public int Attempts => Volatile.Read(ref _attempts);

    public int Increment()
    {
        return Interlocked.Increment(ref _attempts);
    }
}

internal sealed record BlockingTask : IBackgroundTask;

internal sealed class BlockingTaskHandler(BlockingProbe probe)
    : IBackgroundTaskHandler<BlockingTask>
{
    public async Task ExecuteAsync(
        BlockingTask task,
        CancellationToken cancellationToken)
    {
        probe.Start();
        await probe.Release.Task.WaitAsync(cancellationToken);
    }
}

internal sealed class BlockingProbe
{
    private int _invocationCount;

    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public void Start()
    {
        Interlocked.Increment(ref _invocationCount);
        Started.TrySetResult();
    }
}

internal sealed record ConcurrentTask(int Value) : IBackgroundTask;

internal sealed class ConcurrentTaskHandler(ConcurrencyProbe probe)
    : IBackgroundTaskHandler<ConcurrentTask>
{
    public async Task ExecuteAsync(
        ConcurrentTask task,
        CancellationToken cancellationToken)
    {
        probe.Enter();

        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(50),
                cancellationToken);
        }
        finally
        {
            probe.Exit();
        }
    }
}

internal sealed class ConcurrencyProbe
{
    private int _current;
    private int _maximum;

    public int MaximumConcurrency => Volatile.Read(ref _maximum);

    public void Enter()
    {
        var current = Interlocked.Increment(ref _current);

        while (true)
        {
            var maximum = Volatile.Read(ref _maximum);

            if (current <= maximum ||
                Interlocked.CompareExchange(ref _maximum, current, maximum) == maximum)
                return;
        }
    }

    public void Exit()
    {
        Interlocked.Decrement(ref _current);
    }
}

internal sealed class TestRecoverySource : IBackgroundTaskRecoverySource
{
    public Task RecoverAsync(
        ISynchronizationEngine engine,
        CancellationToken cancellationToken)
    {
        engine.Enqueue(new RecordedTask(99));
        return Task.CompletedTask;
    }
}
