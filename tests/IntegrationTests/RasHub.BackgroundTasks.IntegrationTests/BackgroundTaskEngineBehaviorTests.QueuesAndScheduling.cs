using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.IntegrationTests;

public sealed partial class BackgroundTaskEngineBehaviorTests
{
    [Fact]
    public async Task Pending_cancellation_reclaims_queue_capacity_before_completion()
    {
        using var host = CreateHost(
            configure: options => options.SynchronizationQueueCapacity = 1);

        var engine = GetEngine(host);
        var first = engine.Enqueue(new RecordedTask(1));

        Assert.True(engine.Cancel(first.Id));
        Assert.Equal(
            BackgroundTaskOutcome.Canceled,
            (await Await(first, TestContext.Current.CancellationToken)).Outcome);

        var replacement = engine.Enqueue(new RecordedTask(2));

        Assert.True(engine.Cancel(replacement.Id));
        Assert.Equal(
            BackgroundTaskOutcome.Canceled,
            (await Await(replacement, TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task Bulk_pending_cancellation_physically_clears_the_lane()
    {
        const int taskCount = 10_000;
        using var host = CreateHost(configure: options =>
        {
            options.SynchronizationQueueCapacity = taskCount;
            options.MaxActiveTasks = taskCount;
            options.MaxCompletedTaskHistory = 1;
        });

        var engine = GetEngine(host);
        var handles = Enumerable.Range(0, taskCount)
            .Select(value => engine.Enqueue(new RecordedTask(value)))
            .ToArray();
        var cancellationToken = TestContext.Current.CancellationToken;

        await Task.Run(
                () =>
                {
                    foreach (var handle in handles)
                        Assert.True(engine.Cancel(handle.Id));
                },
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        var results = await Task.WhenAll(
            handles.Select(handle => handle.WaitAsync(cancellationToken)));

        Assert.All(
            results,
            result => Assert.Equal(
                BackgroundTaskOutcome.Canceled,
                result.Outcome));
        Assert.Equal(0, engine.GetStatistics().SynchronizationQueueLength);
        Assert.Equal(0, engine.GetStatistics().ActiveTasks);
    }

    [Fact]
    public async Task Multiple_workers_wake_and_execute_one_task_each()
    {
        const int workerCount = 8;
        using var host = CreateHost(
            services =>
            {
                services.AddSingleton(new WorkerRendezvous(workerCount));
                services.AddScoped<
                    IBackgroundTaskHandler<RendezvousTask>,
                    RendezvousTaskHandler>();
            },
            options => options.SynchronizationWorkerCount = workerCount);

        var cancellationToken = TestContext.Current.CancellationToken;
        var engine = GetEngine(host);
        var handles = Enumerable.Range(0, workerCount)
            .Select(_ => engine.Enqueue(new RendezvousTask()))
            .ToArray();

        await host.StartAsync(cancellationToken);

        try
        {
            var results = await Task.WhenAll(
                handles.Select(handle => Await(handle, cancellationToken)));

            Assert.All(results, result => Assert.True(result.IsSucceeded));
            Assert.Equal(
                workerCount,
                host.Services.GetRequiredService<WorkerRendezvous>().Entrants);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Multi_worker_fifo_executes_every_entry_exactly_once()
    {
        const int taskCount = 256;
        using var host = CreateHost(configure: options =>
        {
            options.SynchronizationQueueCapacity = taskCount;
            options.SynchronizationWorkerCount = 16;
        });

        var engine = GetEngine(host);
        var handles = Enumerable.Range(0, taskCount)
            .Select(value => engine.Enqueue(new RecordedTask(value)))
            .ToArray();
        var cancellationToken = TestContext.Current.CancellationToken;

        await host.StartAsync(cancellationToken);

        try
        {
            var results = await Task.WhenAll(
                handles.Select(handle => Await(handle, cancellationToken)));
            var recorded = host.Services
                .GetRequiredService<RecordingProbe>()
                .Values;

            Assert.All(results, result => Assert.True(result.IsSucceeded));
            Assert.Equal(taskCount, recorded.Length);
            Assert.Equal(
                Enumerable.Range(0, taskCount),
                recorded.Order());
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Due_accepted_task_reenters_a_full_lane_without_starvation()
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
                options.SynchronizationQueueCapacity = 1;
                options.SynchronizationWorkerCount = 1;
            });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        var probe = host.Services.GetRequiredService<BlockingProbe>();
        var engine = GetEngine(host);
        var running = engine.Enqueue(new BlockingTask());

        try
        {
            await probe.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            var queued = engine.Enqueue(new RecordedTask(1));
            var delayed = engine.Enqueue(
                new RecordedTask(2),
                new BackgroundTaskOptions
                {
                    NotBefore = DateTimeOffset.UtcNow +
                                TimeSpan.FromMilliseconds(50)
                });

            await WaitUntilAsync(
                () => engine.GetStatistics().SynchronizationQueueLength == 2,
                cancellationToken);

            probe.Release.TrySetResult();

            Assert.True((await Await(running, cancellationToken)).IsSucceeded);
            Assert.True((await Await(queued, cancellationToken)).IsSucceeded);
            Assert.True((await Await(delayed, cancellationToken)).IsSucceeded);
        }
        finally
        {
            probe.Release.TrySetResult();
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Equal_due_tasks_reenter_the_lane_in_schedule_order()
    {
        const int taskCount = 32;
        using var host = CreateHost(configure: options =>
            options.SynchronizationWorkerCount = 1);
        var engine = GetEngine(host);
        var dueAt = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(200);
        var options = new BackgroundTaskOptions { NotBefore = dueAt };
        var handles = Enumerable.Range(0, taskCount)
            .Select(value => engine.Enqueue(new RecordedTask(value), options))
            .ToArray();
        var cancellationToken = TestContext.Current.CancellationToken;

        await Task.Delay(250, cancellationToken);
        await host.StartAsync(cancellationToken);

        try
        {
            var results = await Task.WhenAll(
                handles.Select(handle => Await(handle, cancellationToken)));

            Assert.All(results, result => Assert.True(result.IsSucceeded));
            Assert.Equal(
                Enumerable.Range(0, taskCount),
                host.Services.GetRequiredService<RecordingProbe>().Values);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Concurrency_key_handoff_preserves_registered_waiter_order()
    {
        using var host = CreateHost(
            services =>
            {
                services.AddSingleton<KeyedOrderProbe>();
                services.AddScoped<
                    IBackgroundTaskHandler<KeyedOrderTask>,
                    KeyedOrderTaskHandler>();
            },
            options => options.SynchronizationWorkerCount = 2);

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        var engine = GetEngine(host);
        var probe = host.Services.GetRequiredService<KeyedOrderProbe>();
        var options = new BackgroundTaskOptions
        {
            ConcurrencyKey = "ordered-key"
        };
        var first = engine.Enqueue(new KeyedOrderTask(0), options);

        try
        {
            await probe.FirstStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            var second = engine.Enqueue(new KeyedOrderTask(1), options);
            Assert.True((await Await(
                engine.Enqueue(new RecordedTask(101)),
                cancellationToken)).IsSucceeded);

            var third = engine.Enqueue(new KeyedOrderTask(2), options);
            Assert.True((await Await(
                engine.Enqueue(new RecordedTask(102)),
                cancellationToken)).IsSucceeded);

            probe.ReleaseFirst.TrySetResult();
            await probe.SecondStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            var newcomer = engine.Enqueue(new KeyedOrderTask(3), options);
            Assert.True((await Await(
                engine.Enqueue(new RecordedTask(103)),
                cancellationToken)).IsSucceeded);

            probe.ReleaseSecond.TrySetResult();

            var results = await Task.WhenAll(
                Await(first, cancellationToken),
                Await(second, cancellationToken),
                Await(third, cancellationToken),
                Await(newcomer, cancellationToken));

            Assert.All(results, result => Assert.True(result.IsSucceeded));
            Assert.Equal([0, 1, 2, 3], probe.StartOrder);
        }
        finally
        {
            probe.ReleaseFirst.TrySetResult();
            probe.ReleaseSecond.TrySetResult();
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Canceled_concurrency_waiter_is_removed_before_key_handoff()
    {
        using var host = CreateHost(
            services =>
            {
                services.AddSingleton<KeyedOrderProbe>();
                services.AddScoped<
                    IBackgroundTaskHandler<KeyedOrderTask>,
                    KeyedOrderTaskHandler>();
            },
            options => options.SynchronizationWorkerCount = 2);

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        var engine = GetEngine(host);
        var probe = host.Services.GetRequiredService<KeyedOrderProbe>();
        var options = new BackgroundTaskOptions
        {
            ConcurrencyKey = "cancel-waiter-key"
        };
        var first = engine.Enqueue(new KeyedOrderTask(0), options);

        try
        {
            await probe.FirstStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            var canceled = engine.Enqueue(new KeyedOrderTask(1), options);
            Assert.True((await Await(
                engine.Enqueue(new RecordedTask(201)),
                cancellationToken)).IsSucceeded);

            var last = engine.Enqueue(new KeyedOrderTask(2), options);
            Assert.True((await Await(
                engine.Enqueue(new RecordedTask(202)),
                cancellationToken)).IsSucceeded);

            Assert.True(engine.Cancel(canceled.Id));
            Assert.Equal(
                BackgroundTaskOutcome.Canceled,
                (await Await(canceled, cancellationToken)).Outcome);

            probe.ReleaseFirst.TrySetResult();

            Assert.True((await Await(first, cancellationToken)).IsSucceeded);
            Assert.True((await Await(last, cancellationToken)).IsSucceeded);
            Assert.Equal([0, 2], probe.StartOrder);
            Assert.Equal(0, engine.GetStatistics().ActiveTasks);
        }
        finally
        {
            probe.ReleaseFirst.TrySetResult();
            probe.ReleaseSecond.TrySetResult();
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Canceled_granted_waiter_hands_off_and_old_lease_is_stale()
    {
        using var durationRelease = new ManualResetEventSlim();
        var durationReached = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var keyedTaskType = typeof(KeyedOrderTask).FullName;
        var blockedMeasurement = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == BackgroundTaskTelemetry.MeterName &&
                    instrument.Name == "rashub.background_tasks.attempt.duration")
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>(
            (_, _, tags, _) =>
            {
                if (GetTag(tags, "task.type") != keyedTaskType ||
                    Interlocked.CompareExchange(
                        ref blockedMeasurement,
                        1,
                        0) != 0)
                    return;

                durationReached.TrySetResult();
                durationRelease.Wait(TimeSpan.FromSeconds(5));
            });
        listener.Start();

        using var host = CreateHost(
            services =>
            {
                services.AddSingleton<KeyedOrderProbe>();
                services.AddScoped<
                    IBackgroundTaskHandler<KeyedOrderTask>,
                    KeyedOrderTaskHandler>();
                services.AddSingleton<BlockingProbe>();
                services.AddScoped<
                    IBackgroundTaskHandler<BlockingTask>,
                    BlockingTaskHandler>();
            },
            options => options.SynchronizationWorkerCount = 2);

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        var engine = GetEngine(host);
        var probe = host.Services.GetRequiredService<KeyedOrderProbe>();
        var blockingProbe = host.Services.GetRequiredService<BlockingProbe>();
        var options = new BackgroundTaskOptions
        {
            ConcurrencyKey = "granted-cancel-key"
        };
        var first = engine.Enqueue(new KeyedOrderTask(0), options);

        try
        {
            await probe.FirstStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            var granted = engine.Enqueue(new KeyedOrderTask(1), options);
            Assert.True((await Await(
                engine.Enqueue(new RecordedTask(301)),
                cancellationToken)).IsSucceeded);

            var next = engine.Enqueue(new KeyedOrderTask(2), options);
            Assert.True((await Await(
                engine.Enqueue(new RecordedTask(302)),
                cancellationToken)).IsSucceeded);

            var last = engine.Enqueue(new KeyedOrderTask(3), options);
            Assert.True((await Await(
                engine.Enqueue(new RecordedTask(303)),
                cancellationToken)).IsSucceeded);

            var workerBlock = engine.Enqueue(new BlockingTask());
            await blockingProbe.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            probe.ReleaseFirst.TrySetResult();
            await durationReached.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            Assert.True(engine.Cancel(granted.Id));
            Assert.Equal(
                BackgroundTaskOutcome.Canceled,
                (await Await(granted, cancellationToken)).Outcome);

            durationRelease.Set();

            Assert.True((await Await(first, cancellationToken)).IsSucceeded);
            Assert.True((await Await(next, cancellationToken)).IsSucceeded);
            Assert.True((await Await(last, cancellationToken)).IsSucceeded);
            Assert.Equal([0, 2, 3], probe.StartOrder);

            blockingProbe.Release.TrySetResult();
            Assert.True((await Await(workerBlock, cancellationToken)).IsSucceeded);
            Assert.Equal(0, engine.GetStatistics().ActiveTasks);
        }
        finally
        {
            durationRelease.Set();
            probe.ReleaseFirst.TrySetResult();
            probe.ReleaseSecond.TrySetResult();
            blockingProbe.Release.TrySetResult();
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Immediate_keyed_retry_is_published_after_lease_release()
    {
        using var attemptDurationRelease = new ManualResetEventSlim();
        var attemptDurationReached = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retryTaskType = typeof(KeyedImmediateRetryTask).FullName;
        var blockedMeasurement = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == BackgroundTaskTelemetry.MeterName &&
                    instrument.Name == "rashub.background_tasks.attempt.duration")
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>(
            (_, _, tags, _) =>
            {
                if (GetTag(tags, "task.type") != retryTaskType ||
                    Interlocked.CompareExchange(
                        ref blockedMeasurement,
                        1,
                        0) != 0)
                    return;

                attemptDurationReached.TrySetResult();
                attemptDurationRelease.Wait(TimeSpan.FromSeconds(5));
            });
        listener.Start();

        using var host = CreateHost(
            services =>
            {
                services.AddSingleton<KeyedImmediateRetryProbe>();
                services.AddScoped<
                    IBackgroundTaskHandler<KeyedImmediateRetryTask>,
                    KeyedImmediateRetryTaskHandler>();
            },
            options =>
            {
                options.SynchronizationQueueCapacity = 128;
                options.SynchronizationWorkerCount = 2;
            });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);
        var engine = GetEngine(host);
        var retry = engine.Enqueue(
            new KeyedImmediateRetryTask(),
            new BackgroundTaskOptions
            {
                ConcurrencyKey = "immediate-retry-key",
                MaxAttempts = 2,
                RetryDelay = TimeSpan.Zero
            });

        try
        {
            await attemptDurationReached.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            // The attempt-duration callback holds the first worker inside the
            // keyed lease. In the broken ordering the retry was already visible
            // here, giving the idle second worker time to dequeue and discard it.
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);

            attemptDurationRelease.Set();

            Assert.True((await Await(retry, cancellationToken)).IsSucceeded);
            Assert.Equal(
                2,
                host.Services
                    .GetRequiredService<KeyedImmediateRetryProbe>()
                    .Attempts);
        }
        finally
        {
            attemptDurationRelease.Set();
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public void Canceled_far_future_task_releases_its_payload()
    {
        using var host = CreateHost(configure: options =>
        {
            options.MaxCompletedTaskHistory = 1;
            options.CompletedTaskRetention = TimeSpan.FromHours(1);
        });

        var engine = GetEngine(host);
        var weakPayload = EnqueueAndCancelPayload(engine);

        _ = EnqueueAndCancelPayload(engine);

        ForceFullCollection();

        Assert.False(weakPayload.IsAlive);
    }

    [Fact]
    public void Removed_schedule_releases_its_factory_closure()
    {
        using var host = CreateHost();
        var scheduler = host.Services
            .GetRequiredService<IBackgroundTaskScheduler>();

        var weakPayload = ScheduleAndRemovePayload(scheduler);

        ForceFullCollection();

        Assert.False(weakPayload.IsAlive);
    }

    [Fact]
    public void Schedule_interval_outside_utc_range_is_rejected_at_registration()
    {
        using var host = CreateHost();
        var scheduler = host.Services
            .GetRequiredService<IBackgroundTaskScheduler>();

        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Schedule(
            "overflowing-schedule",
            () => new RecordedTask(1),
            TimeSpan.MaxValue,
            runImmediately: true));

        Assert.Empty(scheduler.GetSchedules());
    }

    [Fact]
    public async Task Remove_waits_for_inflight_dispatch_and_prevents_later_runs()
    {
        using var host = CreateHost();
        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        using var factoryRelease = new ManualResetEventSlim();
        var factoryEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var scheduler = host.Services
            .GetRequiredService<IBackgroundTaskScheduler>();

        using var schedule = scheduler.Schedule(
            "remove-dispatch-boundary",
            () =>
            {
                Interlocked.Increment(ref invocationCount);
                factoryEntered.TrySetResult();
                factoryRelease.Wait(cancellationToken);
                return new RecordedTask(42);
            },
            TimeSpan.FromMilliseconds(50),
            runImmediately: true);

        try
        {
            await factoryEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            var removeStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var removeTask = Task.Run(
                () =>
                {
                    removeStarted.TrySetResult();
                    return scheduler.Remove(schedule.Id);
                },
                cancellationToken);

            await removeStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            await Task.Delay(50, cancellationToken);
            Assert.False(removeTask.IsCompleted);

            factoryRelease.Set();
            Assert.True(await removeTask);

            await Task.Delay(150, cancellationToken);
            Assert.Equal(1, Volatile.Read(ref invocationCount));
        }
        finally
        {
            factoryRelease.Set();
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Scheduler_shutdown_clears_prestart_schedules_and_rejects_new_ones()
    {
        using var host = CreateHost();
        var scheduler = host.Services
            .GetRequiredService<IBackgroundTaskScheduler>();
        var weakPayload = SchedulePayloadForShutdown(scheduler);

        Assert.Single(scheduler.GetSchedules());

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);
        await host.StopAsync(cancellationToken);

        Assert.Empty(scheduler.GetSchedules());
        Assert.Throws<BackgroundTaskRejectedException>(() =>
            scheduler.Schedule(
                "after-stop",
                () => new RecordedTask(2),
                TimeSpan.FromMinutes(1)));

        ForceFullCollection();
        Assert.False(weakPayload.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference EnqueueAndCancelPayload(
        IBackgroundTaskEngine engine)
    {
        var payload = new object();
        var weakPayload = new WeakReference(payload);
        var handle = engine.Enqueue(
            new PayloadTask(payload),
            new BackgroundTaskOptions
            {
                NotBefore = DateTimeOffset.UtcNow + TimeSpan.FromDays(365)
            });

        Assert.True(engine.Cancel(handle.Id));
        Assert.Equal(
            BackgroundTaskOutcome.Canceled,
            handle.WaitAsync().GetAwaiter().GetResult().Outcome);

        return weakPayload;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ScheduleAndRemovePayload(
        IBackgroundTaskScheduler scheduler)
    {
        var payload = new object();
        var weakPayload = new WeakReference(payload);
        var schedule = scheduler.Schedule(
            $"payload-{Guid.NewGuid():N}",
            () =>
            {
                GC.KeepAlive(payload);
                return new RecordedTask(1);
            },
            TimeSpan.FromDays(365));

        schedule.Dispose();
        return weakPayload;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SchedulePayloadForShutdown(
        IBackgroundTaskScheduler scheduler)
    {
        var payload = new object();
        var weakPayload = new WeakReference(payload);

        _ = scheduler.Schedule(
            "shutdown-payload",
            () =>
            {
                GC.KeepAlive(payload);
                return new RecordedTask(1);
            },
            TimeSpan.FromDays(365));

        return weakPayload;
    }

    private static void ForceFullCollection()
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);

        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Expected background task condition was not reached.");

            await Task.Delay(10, cancellationToken);
        }
    }

    private sealed record PayloadTask(object Payload) : IBackgroundTask;

    private sealed record RendezvousTask : IBackgroundTask;

    private sealed record KeyedOrderTask(int Value) : IBackgroundTask;

    private sealed record KeyedImmediateRetryTask : IBackgroundTask;

    private sealed class KeyedImmediateRetryProbe
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public int Increment()
        {
            return Interlocked.Increment(ref _attempts);
        }
    }

    private sealed class KeyedImmediateRetryTaskHandler(
        KeyedImmediateRetryProbe probe)
        : IBackgroundTaskHandler<KeyedImmediateRetryTask>
    {
        public Task ExecuteAsync(
            KeyedImmediateRetryTask task,
            CancellationToken cancellationToken)
        {
            if (probe.Increment() == 1)
                throw new InvalidOperationException("Immediate retry expected.");

            return Task.CompletedTask;
        }
    }

    private sealed class KeyedOrderTaskHandler(KeyedOrderProbe probe)
        : IBackgroundTaskHandler<KeyedOrderTask>
    {
        public async Task ExecuteAsync(
            KeyedOrderTask task,
            CancellationToken cancellationToken)
        {
            probe.Start(task.Value);

            if (task.Value == 0)
                await probe.ReleaseFirst.Task.WaitAsync(cancellationToken);
            else if (task.Value == 1)
                await probe.ReleaseSecond.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class KeyedOrderProbe
    {
        private readonly object _sync = new();
        private readonly List<int> _startOrder = [];

        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSecond { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int[] StartOrder
        {
            get
            {
                lock (_sync)
                {
                    return [.. _startOrder];
                }
            }
        }

        public void Start(int value)
        {
            lock (_sync)
            {
                _startOrder.Add(value);
            }

            if (value == 0)
                FirstStarted.TrySetResult();
            else if (value == 1)
                SecondStarted.TrySetResult();
        }
    }

    private sealed class RendezvousTaskHandler(WorkerRendezvous rendezvous)
        : IBackgroundTaskHandler<RendezvousTask>
    {
        public Task ExecuteAsync(
            RendezvousTask task,
            CancellationToken cancellationToken)
        {
            return rendezvous.EnterAsync(cancellationToken);
        }
    }

    private sealed class WorkerRendezvous(int expectedEntrants)
    {
        private readonly TaskCompletionSource _allEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _entrants;

        public int Entrants => Volatile.Read(ref _entrants);

        public async Task EnterAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _entrants) == expectedEntrants)
                _allEntered.TrySetResult();

            await _allEntered.Task.WaitAsync(cancellationToken);
        }
    }
}
