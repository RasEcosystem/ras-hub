using System.Threading.Channels;
using RasHub.BackgroundTasks.Internal.Execution;

namespace RasHub.BackgroundTasks.Internal.Queues;

/// <summary>
///     Holds delayed executions and returns them to their target queue when their due time arrives.
/// </summary>
internal sealed class BackgroundTaskRescheduler
{
    private static readonly TimeSpan MaximumTimerSlice =
        TimeSpan.FromDays(1);

    private readonly Channel<byte> _changed = CreateChangeChannel();
    private readonly IBackgroundTaskQueue _queue;

    private readonly PriorityQueue<
        BackgroundTaskExecution,
        (DateTimeOffset DueAt, long Sequence)> _scheduled =
        new();

    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;

    private long _nextSequence;

    public BackgroundTaskRescheduler(
        IBackgroundTaskQueue queue,
        TimeProvider timeProvider)
    {
        _queue = queue;
        _timeProvider = timeProvider;
    }

    /// <summary>Number of non-terminal executions waiting for a future enqueue time.</summary>
    public int DelayedExecutionCount
    {
        get
        {
            lock (_sync)
            {
                return _scheduled.UnorderedItems.Count(item =>
                    !item.Element.IsTerminal);
            }
        }
    }

    public int GetOverdueExecutionCount(DateTimeOffset now)
    {
        lock (_sync)
        {
            return _scheduled.UnorderedItems.Count(item =>
                item.Priority.DueAt <= now &&
                !item.Element.IsTerminal);
        }
    }

    public void Schedule(
        BackgroundTaskExecution execution,
        DateTimeOffset dueAt)
    {
        ArgumentNullException.ThrowIfNull(execution);

        lock (_sync)
        {
            // TryRemove uses the same lock after publishing a terminal state.
            // Either it removes this entry, or this recheck prevents the entry.
            if (execution.IsTerminal)
                return;

            _scheduled.Enqueue(
                execution,
                (dueAt, _nextSequence++));
        }

        SignalChanged();
    }

    public bool TryRemove(BackgroundTaskExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        bool removed;

        lock (_sync)
        {
            removed = _scheduled.Remove(
                execution,
                out _,
                out _);
        }

        if (removed)
            SignalChanged();

        return removed;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            BackgroundTaskExecution? execution;
            (DateTimeOffset DueAt, long Sequence) priority;

            lock (_sync)
            {
                if (!_scheduled.TryPeek(out execution, out priority))
                    execution = null;
            }

            if (execution is null)
            {
                await _changed.Reader.ReadAsync(stoppingToken);
                continue;
            }

            var delay = priority.DueAt - _timeProvider.GetUtcNow();

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

                var changedTask = _changed.Reader
                    .ReadAsync(waitCancellation.Token)
                    .AsTask();

                await Task.WhenAny(delayTask, changedTask);
                await waitCancellation.CancelAsync();

                try
                {
                    await Task.WhenAll(delayTask, changedTask);
                }
                catch (OperationCanceledException)
                    when (!stoppingToken.IsCancellationRequested)
                {
                    // The wait that did not win must finish before the next
                    // iteration so it cannot consume a later change signal.
                }

                continue;
            }

            lock (_sync)
            {
                _scheduled.TryDequeue(out execution, out priority);
            }

            if (execution is null)
                continue;

            // UTC can move backwards between the due check and dequeue. Put the
            // actual entry back instead of dispatching it before its due time.
            if (priority.DueAt > _timeProvider.GetUtcNow())
            {
                Schedule(execution, priority.DueAt);
                continue;
            }

            if (!execution.IsTerminal)
                _queue.EnqueueAccepted(execution);
        }
    }

    private static Channel<byte> CreateChangeChannel()
    {
        return Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    private void SignalChanged()
    {
        _changed.Writer.TryWrite(0);
    }
}