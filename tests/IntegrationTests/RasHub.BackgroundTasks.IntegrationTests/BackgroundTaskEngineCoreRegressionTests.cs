using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.IntegrationTests;

public sealed partial class BackgroundTaskEngineBehaviorTests
{
    [Fact]
    public async Task Terminal_result_releases_capacity_before_it_is_observed()
    {
        using var host = CreateHost(configure: options =>
        {
            options.MaxActiveTasks = 1;
            options.MaxCompletedTaskHistory = 3;
        });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);

            for (var value = 1; value <= 10; value++)
            {
                var result = await Await(
                    engine.Enqueue(new RecordedTask(value)),
                    cancellationToken);
                var statistics = engine.GetStatistics();

                Assert.True(result.IsSucceeded);
                Assert.Equal(0, statistics.ActiveTasks);
                Assert.Equal(
                    Math.Min(value, 3),
                    statistics.CompletedTaskHistory);
                Assert.Equal(
                    value,
                    statistics.SynchronizationCompletedTasks);
            }
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Cancellation_followed_by_non_cancellation_failure_returns_canceled()
    {
        long canceledMeasurements = 0;
        using var listener = CreateCanceledTaskListener(
            typeof(CancellationWrappingTask),
            () => Interlocked.Increment(ref canceledMeasurements));
        using var host = CreateHost(services =>
        {
            services.AddSingleton<CancellationWrappingProbe>();
            services.AddScoped<
                IBackgroundTaskHandler<CancellationWrappingTask>,
                CancellationWrappingTaskHandler>();
        });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var handle = engine.Enqueue(new CancellationWrappingTask());
            var probe = host.Services
                .GetRequiredService<CancellationWrappingProbe>();

            await probe.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            Assert.True(engine.Cancel(handle.Id));
            Assert.False(engine.Cancel(handle.Id));

            var result = await Await(handle, cancellationToken);

            Assert.Equal(BackgroundTaskOutcome.Canceled, result.Outcome);
            Assert.Null(result.Exception);
            Assert.Equal(1, Volatile.Read(ref canceledMeasurements));
            Assert.Equal(0, engine.GetStatistics().ActiveTasks);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Throwing_cancellation_callback_does_not_escape_cancel()
    {
        using var host = CreateHost(services =>
        {
            services.AddSingleton<ThrowingCancellationCallbackProbe>();
            services.AddScoped<
                IBackgroundTaskHandler<ThrowingCancellationCallbackTask>,
                ThrowingCancellationCallbackTaskHandler>();
        });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var handle = engine.Enqueue(new ThrowingCancellationCallbackTask());
            var probe = host.Services
                .GetRequiredService<ThrowingCancellationCallbackProbe>();

            await probe.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            var exception = Record.Exception(() => engine.Cancel(handle.Id));

            Assert.Null(exception);
            Assert.Equal(
                BackgroundTaskOutcome.Canceled,
                (await Await(handle, cancellationToken)).Outcome);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Blocking_cancellation_callback_does_not_delay_cancel_return()
    {
        using var probe = new CancellationFanOutProbe(1);
        using var host = CreateHost(services =>
        {
            services.AddSingleton(probe);
            services.AddScoped<
                IBackgroundTaskHandler<CancellationFanOutTask>,
                CancellationFanOutTaskHandler>();
        });
        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var handle = engine.Enqueue(
                new CancellationFanOutTask(0, true),
                new BackgroundTaskOptions { Timeout = null });
            await probe.AllStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            var cancelCall = Task.Run(
                () => engine.Cancel(handle.Id),
                CancellationToken.None);
            await probe.BlockingCallbackEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            var returnedBeforeRelease = false;
            try
            {
                returnedBeforeRelease = await cancelCall.WaitAsync(
                    TimeSpan.FromSeconds(1),
                    cancellationToken);
            }
            catch (TimeoutException)
            {
            }

            probe.ReleaseBlockingCallback();

            Assert.True(await cancelCall);
            Assert.True(returnedBeforeRelease);
            Assert.Equal(
                BackgroundTaskOutcome.Canceled,
                (await Await(handle, cancellationToken)).Outcome);
        }
        finally
        {
            probe.ReleaseBlockingCallback();
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Shutdown_cancellation_fans_out_before_blocking_callback_finishes()
    {
        using var probe = new CancellationFanOutProbe(2);
        using var host = CreateHost(
            services =>
            {
                services.AddSingleton(probe);
                services.AddScoped<
                    IBackgroundTaskHandler<CancellationFanOutTask>,
                    CancellationFanOutTaskHandler>();
            },
            options => options.SynchronizationWorkerCount = 2);
        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);
        var engine = GetEngine(host);
        var options = new BackgroundTaskOptions { Timeout = null };
        var blocking = engine.Enqueue(
            new CancellationFanOutTask(0, true),
            options);
        var observing = engine.Enqueue(
            new CancellationFanOutTask(1, false),
            options);
        await probe.AllStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            cancellationToken);

        var stopTask = Task.Run(
            async () => await host.StopAsync(CancellationToken.None),
            CancellationToken.None);

        try
        {
            await probe.BlockingCallbackEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            await probe.NonBlockingCallbackInvoked.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            Assert.False(stopTask.IsCompleted);

            probe.ReleaseBlockingCallback();
            await stopTask.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            Assert.Equal(
                BackgroundTaskOutcome.Canceled,
                (await Await(blocking, cancellationToken)).Outcome);
            Assert.Equal(
                BackgroundTaskOutcome.Canceled,
                (await Await(observing, cancellationToken)).Outcome);
        }
        finally
        {
            probe.ReleaseBlockingCallback();
            await stopTask.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
        }
    }

    [Fact]
    public async Task Pending_cancellation_keeps_capacity_until_callbacks_finish()
    {
        using var probe = new PendingCancellationProbe();
        using var host = CreateHost(
            services =>
            {
                services.AddSingleton(probe);
                services.AddScoped<
                    IBackgroundTaskHandler<PendingCancellationTask>,
                    PendingCancellationTaskHandler>();
            },
            options => options.MaxActiveTasks = 1);
        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var handle = engine.Enqueue(
                new PendingCancellationTask(),
                new BackgroundTaskOptions
                {
                    MaxAttempts = 2,
                    RetryDelay = TimeSpan.FromHours(1),
                    MaxRetryDelay = TimeSpan.FromHours(1),
                    Timeout = null
                });
            await probe.CallbackRegistered.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            using var pendingTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            pendingTimeout.CancelAfter(TimeSpan.FromSeconds(5));

            while (engine.GetTask(handle.Id)?.State !=
                   BackgroundTaskState.Pending)
                await Task.Delay(10, pendingTimeout.Token);

            Assert.True(engine.Cancel(handle.Id));
            await probe.CallbackEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            Assert.False(handle.WaitAsync(CancellationToken.None).IsCompleted);
            Assert.Equal(1, engine.GetStatistics().ActiveTasks);
            Assert.Throws<BackgroundTaskRejectedException>(() =>
                engine.Enqueue(new RecordedTask(99)));

            probe.ReleaseCallback();

            Assert.Equal(
                BackgroundTaskOutcome.Canceled,
                (await Await(handle, cancellationToken)).Outcome);
            Assert.Equal(0, engine.GetStatistics().ActiveTasks);
        }
        finally
        {
            probe.ReleaseCallback();
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Successful_retry_clears_previous_error()
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
            var engine = GetEngine(host);
            var handle = engine.Enqueue(
                new RetryTask(1),
                new BackgroundTaskOptions { MaxAttempts = 2, RetryDelay = TimeSpan.Zero });

            Assert.True((await Await(handle, cancellationToken)).IsSucceeded);
            Assert.Null(engine.GetTask(handle.Id)?.LastError);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Throwing_exception_message_does_not_break_finalization_or_snapshots()
    {
        using var host = CreateHost(services =>
            services.AddScoped<
                IBackgroundTaskHandler<ThrowingMessageTask>,
                ThrowingMessageTaskHandler>());
        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var failed = engine.Enqueue(
                new ThrowingMessageTask(),
                new BackgroundTaskOptions { MaxAttempts = 1 });
            var result = await Await(failed, cancellationToken);

            Assert.Equal(BackgroundTaskOutcome.Failed, result.Outcome);
            Assert.IsType<ThrowingMessageException>(result.Exception);
            Assert.Equal(
                typeof(ThrowingMessageException).FullName,
                engine.GetTask(failed.Id)?.LastError);
            Assert.True((await Await(
                engine.Enqueue(new RecordedTask(42)),
                cancellationToken)).IsSucceeded);
            Assert.Equal(0, engine.GetStatistics().ActiveTasks);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public void Unsupported_timeout_is_rejected_before_admission()
    {
        using var host = CreateHost();
        var engine = GetEngine(host);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            engine.Enqueue(
                new RecordedTask(1),
                new BackgroundTaskOptions { Timeout = TimeSpan.FromMilliseconds(uint.MaxValue) }));
        Assert.Equal(0, engine.GetStatistics().ActiveTasks);
        Assert.Empty(engine.GetTasks());
    }

    [Fact]
    public async Task Extreme_retry_delay_does_not_stop_worker()
    {
        using var host = CreateHost(
            services =>
            {
                services.AddSingleton<ExtremeRetryProbe>();
                services.AddScoped<
                    IBackgroundTaskHandler<ExtremeRetryTask>,
                    ExtremeRetryTaskHandler>();
            },
            options => options.SynchronizationWorkerCount = 1);

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var delayed = engine.Enqueue(
                new ExtremeRetryTask(),
                new BackgroundTaskOptions
                {
                    MaxAttempts = 2,
                    RetryDelay = TimeSpan.MaxValue,
                    MaxRetryDelay = TimeSpan.MaxValue,
                    Timeout = null
                });

            await host.Services
                .GetRequiredService<ExtremeRetryProbe>()
                .Attempted.Task.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);

            Assert.True((await Await(
                engine.Enqueue(new RecordedTask(42)),
                cancellationToken)).IsSucceeded);
            Assert.True(engine.Cancel(delayed.Id));
            Assert.Equal(
                BackgroundTaskOutcome.Canceled,
                (await Await(delayed, cancellationToken)).Outcome);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Maximum_retention_does_not_stop_cleanup_loop()
    {
        using var host = CreateHost(configure: options =>
        {
            options.CompletedTaskRetention = TimeSpan.MaxValue;
            options.RegistryCleanupInterval = TimeSpan.FromMilliseconds(5);
        });

        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            Assert.True((await Await(
                engine.Enqueue(new RecordedTask(1)),
                cancellationToken)).IsSucceeded);

            await Task.Delay(TimeSpan.FromMilliseconds(30), cancellationToken);

            Assert.True((await Await(
                engine.Enqueue(new RecordedTask(2)),
                cancellationToken)).IsSucceeded);
            Assert.True(engine.GetStatistics().CompletedTaskHistory >= 0);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Full_queue_never_publishes_deduplicated_handle()
    {
        using var host = CreateHost(
            configure: options => options.SynchronizationQueueCapacity = 1);
        var engine = GetEngine(host);
        var filler = engine.Enqueue(new RecordedTask(-1));

        for (var round = 0; round < 10; round++)
        {
            var start = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var options = new BackgroundTaskOptions { DeduplicationKey = $"saturated:{round}" };
            var contenders = Enumerable.Range(0, 64)
                .Select(_ => Task.Run(async () =>
                {
                    await start.Task;

                    try
                    {
                        return engine.Enqueue(new RecordedTask(round), options);
                    }
                    catch (BackgroundTaskRejectedException)
                    {
                        return null;
                    }
                }))
                .ToArray();

            start.TrySetResult();
            var handles = await Task.WhenAll(contenders);

            Assert.All(handles, Assert.Null);
        }

        Assert.True(engine.Cancel(filler.Id));
        Assert.Equal(
            BackgroundTaskOutcome.Canceled,
            (await Await(
                filler,
                TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task Rejected_admission_is_never_visible_to_cancellation()
    {
        const int producerCount = 8;
        const int attemptsPerProducer = 2_000;
        using var host = CreateHost(
            configure: options => options.SynchronizationQueueCapacity = 1);
        var engine = GetEngine(host);
        var filler = engine.Enqueue(new RecordedTask(-1));
        var start = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var scanningCancellation = new CancellationTokenSource();
        var canceledRejectedAdmissions = 0;
        var unexpectedlyAccepted = 0;

        var scanners = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(async () =>
            {
                await start.Task;

                while (!scanningCancellation.IsCancellationRequested)
                    foreach (var snapshot in engine.GetTasks())
                        if (snapshot.Id != filler.Id &&
                            engine.Cancel(snapshot.Id))
                            Interlocked.Increment(
                                ref canceledRejectedAdmissions);
            }))
            .ToArray();

        var producers = Enumerable.Range(0, producerCount)
            .Select(producer => Task.Run(async () =>
            {
                await start.Task;

                for (var attempt = 0;
                     attempt < attemptsPerProducer;
                     attempt++)
                    try
                    {
                        var handle = engine.Enqueue(
                            new RecordedTask(producer));
                        Interlocked.Increment(ref unexpectedlyAccepted);
                        engine.Cancel(handle.Id);
                    }
                    catch (BackgroundTaskRejectedException)
                    {
                    }
            }))
            .ToArray();

        start.TrySetResult();
        await Task.WhenAll(producers);
        scanningCancellation.Cancel();
        await Task.WhenAll(scanners);

        Assert.Equal(0, Volatile.Read(ref canceledRejectedAdmissions));
        Assert.Equal(0, Volatile.Read(ref unexpectedlyAccepted));
        Assert.Equal(1, engine.GetStatistics().ActiveTasks);
        Assert.Equal(filler.Id, Assert.Single(engine.GetTasks()).Id);

        Assert.True(engine.Cancel(filler.Id));
        Assert.Equal(
            BackgroundTaskOutcome.Canceled,
            (await Await(
                filler,
                TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(0, engine.GetStatistics().ActiveTasks);
    }

    private static MeterListener CreateCanceledTaskListener(
        Type taskType,
        Action recordMeasurement)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "RasHub.BackgroundTasks" &&
                instrument.Name == "rashub.background_tasks.canceled")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "task.type" &&
                    Equals(tag.Value, taskType.FullName))
                {
                    recordMeasurement();
                    break;
                }
        });
        listener.Start();
        return listener;
    }
}

internal sealed record CancellationWrappingTask : IBackgroundTask;

internal sealed class CancellationWrappingProbe
{
    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class CancellationWrappingTaskHandler(
    CancellationWrappingProbe probe)
    : IBackgroundTaskHandler<CancellationWrappingTask>
{
    public async Task ExecuteAsync(
        CancellationWrappingTask task,
        CancellationToken cancellationToken)
    {
        probe.Started.TrySetResult();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("Wrapped cancellation.");
        }
    }
}

internal sealed record ThrowingCancellationCallbackTask : IBackgroundTask;

internal sealed record CancellationFanOutTask(
    int Id,
    bool BlocksCallback) : IBackgroundTask;

internal sealed class CancellationFanOutProbe(int expectedStarts) : IDisposable
{
    private readonly ManualResetEventSlim _blockingRelease = new();
    private int _started;

    public TaskCompletionSource AllStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource BlockingCallbackEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource NonBlockingCallbackInvoked { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        _blockingRelease.Dispose();
    }

    public void MarkStarted()
    {
        if (Interlocked.Increment(ref _started) == expectedStarts)
            AllStarted.TrySetResult();
    }

    public void OnCancellation(CancellationFanOutTask task)
    {
        if (!task.BlocksCallback)
        {
            NonBlockingCallbackInvoked.TrySetResult();
            return;
        }

        BlockingCallbackEntered.TrySetResult();
        _blockingRelease.Wait();
    }

    public void ReleaseBlockingCallback()
    {
        _blockingRelease.Set();
    }
}

internal sealed class CancellationFanOutTaskHandler(
    CancellationFanOutProbe probe)
    : IBackgroundTaskHandler<CancellationFanOutTask>
{
    public async Task ExecuteAsync(
        CancellationFanOutTask task,
        CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() => probe.OnCancellation(task));
        probe.MarkStarted();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}

internal sealed record PendingCancellationTask : IBackgroundTask;

internal sealed class PendingCancellationTaskHandler(
    PendingCancellationProbe probe)
    : IBackgroundTaskHandler<PendingCancellationTask>
{
    public Task ExecuteAsync(
        PendingCancellationTask task,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken.Register(probe.BlockCallback);
        probe.CallbackRegistered.TrySetResult();
        throw new InvalidOperationException("Expected retryable failure.");
    }
}

internal sealed class PendingCancellationProbe : IDisposable
{
    private readonly ManualResetEventSlim _callbackRelease = new();

    public TaskCompletionSource CallbackEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource CallbackRegistered { get; } =
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

internal sealed class ThrowingCancellationCallbackProbe
{
    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class ThrowingCancellationCallbackTaskHandler(
    ThrowingCancellationCallbackProbe probe)
    : IBackgroundTaskHandler<ThrowingCancellationCallbackTask>
{
    public async Task ExecuteAsync(
        ThrowingCancellationCallbackTask task,
        CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(static () => throw new InvalidOperationException(
            "Cancellation callback failed."));
        probe.Started.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}

internal sealed record ExtremeRetryTask : IBackgroundTask;

internal sealed record ThrowingMessageTask : IBackgroundTask;

internal sealed class ThrowingMessageException : Exception
{
    public override string Message =>
        throw new InvalidOperationException("Message getter failed.");
}

internal sealed class ThrowingMessageTaskHandler
    : IBackgroundTaskHandler<ThrowingMessageTask>
{
    public Task ExecuteAsync(
        ThrowingMessageTask task,
        CancellationToken cancellationToken)
    {
        throw new ThrowingMessageException();
    }
}

internal sealed class ExtremeRetryProbe
{
    public TaskCompletionSource Attempted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class ExtremeRetryTaskHandler(ExtremeRetryProbe probe)
    : IBackgroundTaskHandler<ExtremeRetryTask>
{
    public Task ExecuteAsync(
        ExtremeRetryTask task,
        CancellationToken cancellationToken)
    {
        probe.Attempted.TrySetResult();
        throw new InvalidOperationException("Retry expected.");
    }
}