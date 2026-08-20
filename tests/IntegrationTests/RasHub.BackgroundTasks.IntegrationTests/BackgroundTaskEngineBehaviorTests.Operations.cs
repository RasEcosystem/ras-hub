using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.IntegrationTests;

public sealed partial class BackgroundTaskEngineBehaviorTests
{
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
                new BackgroundTaskOptions { Queue = BackgroundTaskQueue.Maintenance });

            var blockingProbe = host.Services
                .GetRequiredService<BlockingProbe>();

            await blockingProbe.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            var interactive = engine.Enqueue(
                new RecordedTask(123),
                new BackgroundTaskOptions { Queue = BackgroundTaskQueue.Interactive });

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
            options => options.SynchronizationWorkerCount = 1);

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
    public async Task Shutdown_cancels_every_execution_in_a_full_pending_queue()
    {
        const int rounds = 12;
        const int queueCapacity = 128;
        var cancellationToken = TestContext.Current.CancellationToken;

        for (var round = 0; round < rounds; round++)
        {
            using var host = CreateHost(
                services =>
                {
                    services.AddSingleton<BlockingProbe>();
                    services.AddScoped<
                        IBackgroundTaskHandler<BlockingTask>,
                        BlockingTaskHandler>();
                },
                options =>
                {
                    options.SynchronizationQueueCapacity = queueCapacity;
                    options.SynchronizationWorkerCount = 1;
                });
            await host.StartAsync(cancellationToken);
            var stopped = false;

            try
            {
                var engine = GetEngine(host);
                var options = new BackgroundTaskOptions { Timeout = null };
                var running = engine.Enqueue(new BlockingTask(), options);
                await host.Services
                    .GetRequiredService<BlockingProbe>()
                    .Started.Task.WaitAsync(
                        TimeSpan.FromSeconds(5),
                        cancellationToken);
                var pending = Enumerable.Range(0, queueCapacity)
                    .Select(value => engine.Enqueue(
                        new RecordedTask(value),
                        options))
                    .ToArray();

                await host.StopAsync(cancellationToken);
                stopped = true;

                var results = await Task.WhenAll(
                    pending.Prepend(running)
                        .Select(handle => Await(handle, cancellationToken)));

                Assert.All(
                    results,
                    result => Assert.Equal(
                        BackgroundTaskOutcome.Canceled,
                        result.Outcome));
                Assert.Equal(0, engine.GetStatistics().ActiveTasks);
            }
            finally
            {
                if (!stopped)
                    await host.StopAsync(cancellationToken);
            }
        }
    }

    [Fact]
    public async Task Readiness_is_unhealthy_when_queue_capacity_is_exhausted()
    {
        using var host = CreateHost(
            configure: options => options.SynchronizationQueueCapacity = 1);

        GetEngine(host).Enqueue(new RecordedTask(1));

        var report = await host.Services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                registration => registration.Name == "background-tasks",
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

            BackgroundTaskEngineStatistics statistics;
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

    [Fact]
    public async Task Completed_history_evicts_old_entries_without_rejecting_new_work()
    {
        using var host = CreateHost(configure: options =>
        {
            options.MaxActiveTasks = 1;
            options.MaxCompletedTaskHistory = 1;
            options.CompletedTaskRetention = TimeSpan.FromHours(1);
        });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);

            for (var value = 1; value <= 3; value++)
            {
                await Await(
                    engine.Enqueue(new RecordedTask(value)),
                    cancellationToken);

                while (engine.GetStatistics().ActiveTasks != 0)
                    await Task.Delay(10, cancellationToken);
            }

            while (engine.GetStatistics().SynchronizationCompletedTasks < 3)
                await Task.Delay(10, cancellationToken);

            var statistics = engine.GetStatistics();

            Assert.Equal(0, statistics.ActiveTasks);
            Assert.Equal(1, statistics.CompletedTaskHistory);
            Assert.Equal(3, statistics.SynchronizationCompletedTasks);
            Assert.Single(engine.GetTasks());
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }
}