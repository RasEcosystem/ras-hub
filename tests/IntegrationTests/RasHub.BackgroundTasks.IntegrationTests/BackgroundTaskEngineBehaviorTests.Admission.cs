using Microsoft.Extensions.DependencyInjection;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.IntegrationTests;

public sealed partial class BackgroundTaskEngineBehaviorTests
{
    [Fact]
    public async Task Admission_clock_failure_releases_reservation_and_allows_next_enqueue()
    {
        var timeProvider = new AdmissionFaultTimeProvider();
        using var host = CreateHost(services =>
            services.AddSingleton<TimeProvider>(timeProvider));
        var engine = GetEngine(host);
        var options = new BackgroundTaskOptions
        {
            NotBefore = DateTimeOffset.MaxValue,
            Timeout = null
        };
        timeProvider.FailAfterSuccessfulReads(0);

        Assert.Throws<AdmissionClockException>(() =>
            engine.Enqueue(new RecordedTask(1), options));
        Assert.Equal(0, engine.GetStatistics().ActiveTasks);
        Assert.Empty(engine.GetTasks());

        var accepted = engine.Enqueue(new RecordedTask(2), options);
        Assert.True(engine.Cancel(accepted.Id));
        Assert.Equal(
            BackgroundTaskOutcome.Canceled,
            (await Await(
                accepted,
                TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(0, engine.GetStatistics().ActiveTasks);
    }

    [Fact]
    public async Task Queue_clock_failure_rolls_back_deduplication_and_placement()
    {
        var timeProvider = new AdmissionFaultTimeProvider();
        using var host = CreateHost(services =>
            services.AddSingleton<TimeProvider>(timeProvider));
        var engine = GetEngine(host);
        var options = new BackgroundTaskOptions
        {
            DeduplicationKey = "admission-clock-failure",
            Timeout = null
        };
        timeProvider.FailAfterSuccessfulReads(1);

        Assert.Throws<AdmissionClockException>(() =>
            engine.Enqueue(new RecordedTask(1), options));

        var failedStatistics = engine.GetStatistics();
        Assert.Equal(0, failedStatistics.ActiveTasks);
        Assert.Equal(0, failedStatistics.SynchronizationQueueLength);
        Assert.Empty(engine.GetTasks());

        var accepted = engine.Enqueue(new RecordedTask(2), options);
        Assert.True(engine.Cancel(accepted.Id));
        Assert.Equal(
            BackgroundTaskOutcome.Canceled,
            (await Await(
                accepted,
                TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(0, engine.GetStatistics().ActiveTasks);
    }

    private sealed class AdmissionFaultTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private int _successfulReadsBeforeFailure = -1;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                if (_successfulReadsBeforeFailure == 0)
                {
                    _successfulReadsBeforeFailure = -1;
                    throw new AdmissionClockException();
                }

                if (_successfulReadsBeforeFailure > 0)
                    _successfulReadsBeforeFailure--;
            }

            return new DateTimeOffset(
                2026,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero);
        }

        public void FailAfterSuccessfulReads(int successfulReads)
        {
            if (successfulReads < 0)
                throw new ArgumentOutOfRangeException(nameof(successfulReads));

            lock (_sync)
            {
                _successfulReadsBeforeFailure = successfulReads;
            }
        }
    }
}

internal sealed class AdmissionClockException()
    : InvalidOperationException("Expected admission clock failure.");
