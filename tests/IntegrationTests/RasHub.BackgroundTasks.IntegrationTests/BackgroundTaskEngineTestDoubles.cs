using System.Collections.Concurrent;
using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.BackgroundTasks.IntegrationTests;

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