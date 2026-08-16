using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.IntegrationTests;

public sealed partial class BackgroundTaskEngineBehaviorTests
{
    [Fact]
    public async Task Reentrant_enqueued_listener_preserves_active_metric_balance()
    {
        IBackgroundTaskEngine? engine = null;
        long activeBalance = 0;
        var cancellationAccepted = 0;
        var listenerReentered = 0;
        var taskType = typeof(MetricsProbeTask).FullName;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == BackgroundTaskTelemetry.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (GetTag(tags, "task.type") != taskType)
                return;

            if (instrument.Name == "rashub.background_tasks.active")
                Interlocked.Add(ref activeBalance, measurement);

            if (instrument.Name != "rashub.background_tasks.enqueued" ||
                Interlocked.Exchange(ref listenerReentered, 1) != 0)
                return;

            var execution = Assert.Single(engine!.GetTasks());
            if (engine.Cancel(execution.Id))
                Interlocked.Exchange(ref cancellationAccepted, 1);
        });
        listener.Start();

        using var host = CreateHost(services =>
            services.AddScoped<
                IBackgroundTaskHandler<MetricsProbeTask>,
                MetricsProbeTaskHandler>());
        engine = GetEngine(host);

        var handle = engine.Enqueue(new MetricsProbeTask(1));
        var result = await Await(
            handle,
            TestContext.Current.CancellationToken);

        Assert.Equal(BackgroundTaskOutcome.Canceled, result.Outcome);
        Assert.Equal(1, Volatile.Read(ref cancellationAccepted));
        Assert.Equal(0, Interlocked.Read(ref activeBalance));
        Assert.Equal(0, engine.GetStatistics().ActiveTasks);
    }

    [Fact]
    public async Task Throwing_metric_listener_does_not_affect_execution_or_shutdown()
    {
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == BackgroundTaskTelemetry.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(static (_, _, _, _) =>
            throw new InvalidOperationException(
                "Expected metric listener failure."));
        listener.SetMeasurementEventCallback<double>(static (_, _, _, _) =>
            throw new InvalidOperationException(
                "Expected metric listener failure."));
        listener.Start();

        using var host = CreateHost(services =>
            services.AddScoped<
                IBackgroundTaskHandler<MetricsProbeTask>,
                MetricsProbeTaskHandler>());
        var engine = GetEngine(host);
        var admitted = engine.Enqueue(new MetricsProbeTask(1));
        var cancellationToken = TestContext.Current.CancellationToken;

        await host.StartAsync(cancellationToken);

        try
        {
            Assert.True((await Await(admitted, cancellationToken)).IsSucceeded);

            var next = engine.Enqueue(new MetricsProbeTask(2));
            Assert.True((await Await(next, cancellationToken)).IsSucceeded);
            Assert.Equal(0, engine.GetStatistics().ActiveTasks);
        }
        finally
        {
            await host.StopAsync(cancellationToken).WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
        }
    }

    [Fact]
    public async Task Metrics_terminal_tasks_balance_activity_and_publish_runtime_gauges()
    {
        var measurements = new ConcurrentDictionary<
            string,
            ConcurrentBag<double>>(StringComparer.Ordinal);
        long activeBalance = 0;
        var taskType = typeof(MetricsProbeTask).FullName;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == BackgroundTaskTelemetry.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == "rashub.background_tasks.active" &&
                GetTag(tags, "task.type") == taskType)
                Interlocked.Add(ref activeBalance, measurement);
        });
        listener.SetMeasurementEventCallback<int>((instrument, measurement, _, _) =>
            RecordMeasurement(measurements, instrument, measurement));
        listener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
            RecordMeasurement(measurements, instrument, measurement));
        listener.Start();

        using var host = CreateHost(services =>
        {
            services.AddSingleton<BlockingProbe>();
            services.AddScoped<
                IBackgroundTaskHandler<BlockingTask>,
                BlockingTaskHandler>();
            services.AddScoped<
                IBackgroundTaskHandler<MetricsProbeTask>,
                MetricsProbeTaskHandler>();
        });
        var engine = GetEngine(host);
        var queued = engine.Enqueue(new MetricsProbeTask(1));
        var delayed = engine.Enqueue(
            new MetricsProbeTask(2),
            new BackgroundTaskOptions
            {
                NotBefore = DateTimeOffset.UtcNow + TimeSpan.FromDays(1)
            });

        listener.RecordObservableInstruments();

        Assert.Contains(
            measurements["rashub.background_tasks.queue.length"],
            value => value >= 1);
        Assert.Contains(
            measurements["rashub.background_tasks.delayed"],
            value => value >= 1);
        AssertGaugePublished(
            measurements,
            "rashub.background_tasks.queue.oldest.age");
        AssertGaugePublished(
            measurements,
            "rashub.background_tasks.delayed.overdue");
        AssertGaugePublished(
            measurements,
            "rashub.background_tasks.processes.live");
        AssertGaugePublished(
            measurements,
            "rashub.background_tasks.processes.expected");

        Assert.True(engine.Cancel(queued.Id));
        Assert.True(engine.Cancel(delayed.Id));

        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal(
            BackgroundTaskOutcome.Canceled,
            (await Await(queued, cancellationToken)).Outcome);
        Assert.Equal(
            BackgroundTaskOutcome.Canceled,
            (await Await(delayed, cancellationToken)).Outcome);
        Assert.Equal(0, Interlocked.Read(ref activeBalance));

        await host.StartAsync(cancellationToken);

        try
        {
            var succeeded = engine.Enqueue(new MetricsProbeTask(3));
            var failed = engine.Enqueue(
                new MetricsProbeTask(-1),
                new BackgroundTaskOptions { MaxAttempts = 1 });

            Assert.True((await Await(succeeded, cancellationToken)).IsSucceeded);
            Assert.Equal(
                BackgroundTaskOutcome.Failed,
                (await Await(failed, cancellationToken)).Outcome);
            Assert.Equal(0, Interlocked.Read(ref activeBalance));

            var concurrencyOptions = new BackgroundTaskOptions
            {
                ConcurrencyKey = "metrics:gate"
            };
            var first = engine.Enqueue(
                new BlockingTask(),
                concurrencyOptions);
            var second = engine.Enqueue(
                new BlockingTask(),
                concurrencyOptions);
            var probe = host.Services.GetRequiredService<BlockingProbe>();

            await probe.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            await WaitForMeasurementAsync(
                listener,
                measurements,
                "rashub.background_tasks.concurrency.waiters",
                value => value >= 1,
                cancellationToken);

            Assert.Contains(
                measurements[
                    "rashub.background_tasks.concurrency.keys.active"],
                value => value >= 1);
            Assert.Contains(
                measurements["rashub.background_tasks.processes.live"],
                value => value >= 1);
            Assert.Contains(
                measurements[
                    "rashub.background_tasks.processes.expected"],
                value => value >= 1);

            probe.Release.TrySetResult();
            var results = await Task.WhenAll(
                Await(first, cancellationToken),
                Await(second, cancellationToken));
            Assert.All(results, result => Assert.True(result.IsSucceeded));
        }
        finally
        {
            host.Services
                .GetRequiredService<BlockingProbe>()
                .Release
                .TrySetResult();
            await host.StopAsync(cancellationToken);
        }
    }

    private static void AssertGaugePublished(
        ConcurrentDictionary<string, ConcurrentBag<double>> measurements,
        string instrumentName)
    {
        Assert.True(
            measurements.TryGetValue(instrumentName, out var values) &&
            !values.IsEmpty,
            $"Instrument '{instrumentName}' did not publish a measurement.");
    }

    private static string? GetTag(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        string tagName)
    {
        foreach (var tag in tags)
            if (tag.Key == tagName)
                return tag.Value as string;

        return null;
    }

    private static void RecordMeasurement<T>(
        ConcurrentDictionary<string, ConcurrentBag<double>> measurements,
        Instrument instrument,
        T measurement)
        where T : struct, IConvertible
    {
        measurements
            .GetOrAdd(instrument.Name, _ => [])
            .Add(Convert.ToDouble(measurement));
    }

    private static async Task WaitForMeasurementAsync(
        MeterListener listener,
        ConcurrentDictionary<string, ConcurrentBag<double>> measurements,
        string instrumentName,
        Func<double, bool> predicate,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(5));

        while (!measurements.TryGetValue(instrumentName, out var values) ||
               !values.Any(predicate))
        {
            listener.RecordObservableInstruments();
            await Task.Delay(
                TimeSpan.FromMilliseconds(10),
                timeoutCancellation.Token);
        }
    }

    private sealed record MetricsProbeTask(int Value) : IBackgroundTask;

    private sealed class MetricsProbeTaskHandler
        : IBackgroundTaskHandler<MetricsProbeTask>
    {
        public Task ExecuteAsync(
            MetricsProbeTask task,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (task.Value < 0)
                throw new InvalidOperationException("Expected metrics failure.");

            return Task.CompletedTask;
        }
    }
}