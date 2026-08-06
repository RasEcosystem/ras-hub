namespace RasHub.Synchronization.Internal;

internal sealed class BackgroundTaskRescheduler
{
    private static readonly TimeSpan QueueRetryDelay =
        TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan MaximumTimerSlice =
        TimeSpan.FromDays(1);

    private readonly SemaphoreSlim _changed = new(0);
    private readonly IBackgroundTaskQueue _queue;

    private readonly PriorityQueue<BackgroundTaskExecution, DateTimeOffset> _scheduled =
        new();

    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;

    public BackgroundTaskRescheduler(
        IBackgroundTaskQueue queue,
        TimeProvider timeProvider)
    {
        _queue = queue;
        _timeProvider = timeProvider;
    }

    public void Schedule(
        BackgroundTaskExecution execution,
        DateTimeOffset dueAt)
    {
        lock (_sync)
        {
            _scheduled.Enqueue(execution, dueAt);
        }

        _changed.Release();
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            BackgroundTaskExecution? execution;
            DateTimeOffset dueAt;

            lock (_sync)
            {
                if (!_scheduled.TryPeek(out execution, out dueAt))
                    execution = null;
            }

            if (execution is null)
            {
                await _changed.WaitAsync(stoppingToken);
                continue;
            }

            var delay = dueAt - _timeProvider.GetUtcNow();

            if (delay > TimeSpan.Zero)
            {
                delay = delay > MaximumTimerSlice
                    ? MaximumTimerSlice
                    : delay;

                using var waitCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        stoppingToken);

                var delayTask = Task.Delay(
                    delay,
                    _timeProvider,
                    waitCancellation.Token);

                var changedTask = _changed.WaitAsync(
                    waitCancellation.Token);

                await Task.WhenAny(delayTask, changedTask);
                await waitCancellation.CancelAsync();
                continue;
            }

            lock (_sync)
            {
                _scheduled.Dequeue();
            }

            if (execution.IsTerminal)
                continue;

            if (!_queue.TryEnqueue(execution))
                Schedule(
                    execution,
                    _timeProvider.GetUtcNow() + QueueRetryDelay);
        }
    }
}