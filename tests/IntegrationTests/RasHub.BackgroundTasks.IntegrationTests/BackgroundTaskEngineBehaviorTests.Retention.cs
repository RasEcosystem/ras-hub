using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.IntegrationTests;

public sealed partial class BackgroundTaskEngineBehaviorTests
{
    [Fact]
    public async Task Completed_history_releases_payload_and_exception_while_snapshot_remains()
    {
        using var host = CreateHost(
            services =>
            {
                services.AddSingleton<HistoryRetentionProbe>();
                services.AddScoped<
                    IBackgroundTaskHandler<HistoryRetentionTask>,
                    HistoryRetentionTaskHandler>();
            },
            options =>
            {
                options.SynchronizationWorkerCount = 1;
                options.MaxCompletedTaskHistory = 16;
                options.CompletedTaskRetention = TimeSpan.FromHours(1);
            });
        var cancellationToken = TestContext.Current.CancellationToken;
        await host.StartAsync(cancellationToken);

        try
        {
            var engine = GetEngine(host);
            var retained = FailAndDropHandle(
                engine,
                host.Services.GetRequiredService<HistoryRetentionProbe>());

            var snapshot = engine.GetTask(retained.TaskId);
            Assert.NotNull(snapshot);
            Assert.Equal(BackgroundTaskState.Failed, snapshot.State);
            Assert.Contains(
                engine.GetTasks(),
                item => item.Id == retained.TaskId);

            await WaitForCollectionAsync(
                [
                    retained.TaskPayload,
                    retained.ExceptionPayload,
                    retained.Exception
                ],
                cancellationToken);

            snapshot = engine.GetTask(retained.TaskId);
            Assert.NotNull(snapshot);
            Assert.Equal(BackgroundTaskState.Failed, snapshot.State);
            Assert.Equal("Expected history failure.", snapshot.LastError);
            Assert.Equal(1, engine.GetStatistics().CompletedTaskHistory);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public void Non_monotonic_cleanup_physically_removes_expired_history_entries()
    {
        var baseline = new DateTimeOffset(
            2026,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        var timeProvider = new MutableUtcTimeProvider(
            baseline + TimeSpan.FromTicks(1_000));
        using var host = CreateHost(
            services => services.AddSingleton<TimeProvider>(timeProvider),
            options =>
            {
                options.CompletedTaskRetention = TimeSpan.FromTicks(100);
                options.MaxCompletedTaskHistory = 1_000;
            });
        var engine = GetEngine(host);
        var anchorId = EnqueueAndCancelForHistory(engine);

        for (var iteration = 0; iteration < 200; iteration++)
        {
            timeProvider.SetUtcNow(baseline);
            _ = EnqueueAndCancelForHistory(engine);

            timeProvider.SetUtcNow(
                baseline + TimeSpan.FromTicks(200));
            InvokeCompletedHistoryCleanup(engine);
        }

        var snapshot = Assert.Single(engine.GetTasks());
        Assert.Equal(anchorId, snapshot.Id);
        Assert.Equal(
            BackgroundTaskState.Canceled,
            engine.GetTask(anchorId)?.State);
        Assert.Equal(1, engine.GetStatistics().CompletedTaskHistory);
        Assert.Equal(1, GetPhysicalCompletedHistoryCount(engine));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static HistoryRetentionReferences FailAndDropHandle(
        IBackgroundTaskEngine engine,
        HistoryRetentionProbe probe)
    {
        var taskPayload = new object();
        var exceptionPayload = new object();
        var taskPayloadReference = new WeakReference(taskPayload);
        var exceptionPayloadReference = new WeakReference(exceptionPayload);
        var handle = engine.Enqueue(
            new HistoryRetentionTask(taskPayload, exceptionPayload),
            new BackgroundTaskOptions { MaxAttempts = 1, Timeout = null });
        var result = handle.WaitAsync()
            .WaitAsync(TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();

        Assert.Equal(BackgroundTaskOutcome.Failed, result.Outcome);
        Assert.IsType<HistoryRetentionException>(result.Exception);

        return new HistoryRetentionReferences(
            handle.Id,
            taskPayloadReference,
            exceptionPayloadReference,
            probe.GetExceptionReference());
    }

    private static Guid EnqueueAndCancelForHistory(
        IBackgroundTaskEngine engine)
    {
        var handle = engine.Enqueue(
            new RecordedTask(1),
            new BackgroundTaskOptions { NotBefore = DateTimeOffset.MaxValue, Timeout = null });

        Assert.True(engine.Cancel(handle.Id));
        Assert.Equal(
            BackgroundTaskOutcome.Canceled,
            handle.WaitAsync()
                .WaitAsync(TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult()
                .Outcome);

        return handle.Id;
    }

    private static void InvokeCompletedHistoryCleanup(
        IBackgroundTaskEngine engine)
    {
        var cleanup = engine.GetType().GetMethod(
            "CleanupCompletedTasks",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(cleanup);
        cleanup.Invoke(engine, null);
    }

    private static int GetPhysicalCompletedHistoryCount(
        IBackgroundTaskEngine engine)
    {
        var historyField = engine.GetType().GetField(
            "_completedHistory",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(historyField);
        return Assert.IsAssignableFrom<ICollection<Guid>>(
                historyField.GetValue(engine))
            .Count;
    }

    private static async Task WaitForCollectionAsync(
        IReadOnlyList<WeakReference> references,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);

        while (references.Any(reference => reference.IsAlive))
        {
            ForceRetentionCollection();

            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException(
                    "A completed execution still retains its payload or exception graph.");

            await Task.Delay(10, cancellationToken);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceRetentionCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record HistoryRetentionReferences(
        Guid TaskId,
        WeakReference TaskPayload,
        WeakReference ExceptionPayload,
        WeakReference Exception);

    private sealed class MutableUtcTimeProvider(
        DateTimeOffset initialUtcNow) : TimeProvider
    {
        private long _utcTicks = initialUtcNow.UtcTicks;

        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(
                Interlocked.Read(ref _utcTicks),
                TimeSpan.Zero);
        }

        public void SetUtcNow(DateTimeOffset value)
        {
            Interlocked.Exchange(ref _utcTicks, value.UtcTicks);
        }
    }
}

internal sealed record HistoryRetentionTask(
    object TaskPayload,
    object ExceptionPayload) : IBackgroundTask;

internal sealed class HistoryRetentionTaskHandler(
    HistoryRetentionProbe probe)
    : IBackgroundTaskHandler<HistoryRetentionTask>
{
    public Task ExecuteAsync(
        HistoryRetentionTask task,
        CancellationToken cancellationToken)
    {
        GC.KeepAlive(task.TaskPayload);
        var exception = new HistoryRetentionException(
            task.ExceptionPayload);
        probe.Observe(exception);
        throw exception;
    }
}

internal sealed class HistoryRetentionProbe
{
    private WeakReference? _exception;

    public void Observe(Exception exception)
    {
        _exception = new WeakReference(exception);
    }

    public WeakReference GetExceptionReference()
    {
        return _exception ??
               throw new InvalidOperationException(
                   "The history failure was not observed.");
    }
}

internal sealed class HistoryRetentionException(object payload)
    : Exception("Expected history failure.")
{
    public object Payload { get; } = payload;
}
