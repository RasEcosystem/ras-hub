using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Configuration;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.IntegrationTests;

public sealed partial class BackgroundTaskEngineBehaviorTests
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
                new BackgroundTaskOptions { MaxAttempts = 3, RetryDelay = TimeSpan.Zero });

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
                new BackgroundTaskOptions { MaxAttempts = 1, Timeout = TimeSpan.FromMilliseconds(50) });

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
            var options = new BackgroundTaskOptions { DeduplicationKey = "same-gate" };

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
            options => options.SynchronizationWorkerCount = 4);

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var options = new BackgroundTaskOptions { ConcurrencyKey = "gate:42" };

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
    public async Task Tasks_in_the_same_lane_are_dequeued_in_fifo_order()
    {
        using var host = CreateHost(
            configure: options => options.SynchronizationWorkerCount = 1);
        var engine = GetEngine(host);

        var first = engine.Enqueue(new RecordedTask(1));
        var second = engine.Enqueue(new RecordedTask(2));

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            await Task.WhenAll(
                Await(first, cancellationToken),
                Await(second, cancellationToken));

            var values = host.Services
                .GetRequiredService<RecordingProbe>()
                .Values;

            Assert.Equal([1, 2], values);
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
    public void Full_queue_rejects_new_task_without_returning_handle()
    {
        using var host = CreateHost(
            configure: options => options.SynchronizationQueueCapacity = 1);

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
                new BackgroundTaskOptions { MaxAttempts = 10, RetryDelay = TimeSpan.Zero });

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

    private static IHost CreateHost(
        Action<IServiceCollection>? register = null,
        Action<BackgroundTaskEngineOptions>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton<RecordingProbe>();
        builder.Services.AddScoped<
            IBackgroundTaskHandler<RecordedTask>,
            RecordedTaskHandler>();

        register?.Invoke(builder.Services);

        builder.Services.AddRasHubBackgroundTasks(options =>
        {
            options.SynchronizationQueueCapacity = 32;
            options.InteractiveQueueCapacity = 8;
            options.MaintenanceQueueCapacity = 8;
            options.SynchronizationWorkerCount = 2;
            options.InteractiveWorkerCount = 1;
            options.MaintenanceWorkerCount = 1;
            configure?.Invoke(options);
        });

        return builder.Build();
    }

    private static IBackgroundTaskEngine GetEngine(IHost host)
    {
        return host.Services.GetRequiredService<IBackgroundTaskEngine>();
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