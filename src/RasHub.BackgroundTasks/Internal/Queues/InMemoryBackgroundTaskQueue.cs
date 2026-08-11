using System.Threading.Channels;
using RasHub.BackgroundTasks.Configuration;
using RasHub.BackgroundTasks.Internal.Execution;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.Internal.Queues;

internal sealed class InMemoryBackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly IReadOnlyDictionary<BackgroundTaskQueue, FifoLane> _lanes;

    public InMemoryBackgroundTaskQueue(
        BackgroundTaskEngineOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _lanes = new Dictionary<BackgroundTaskQueue, FifoLane>
        {
            [BackgroundTaskQueue.Interactive] = new(
                options.InteractiveQueueCapacity,
                timeProvider),
            [BackgroundTaskQueue.Synchronization] = new(
                options.SynchronizationQueueCapacity,
                timeProvider),
            [BackgroundTaskQueue.Maintenance] = new(
                options.MaintenanceQueueCapacity,
                timeProvider)
        };
    }

    public bool TryEnqueue(BackgroundTaskExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        return GetLane(execution.Options.Queue).TryEnqueue(execution);
    }

    public bool EnqueueAccepted(BackgroundTaskExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        return GetLane(execution.Options.Queue).EnqueueAccepted(execution);
    }

    public bool TryRemove(BackgroundTaskExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        return GetLane(execution.Options.Queue).TryRemove(execution);
    }

    public ValueTask<BackgroundTaskExecution> DequeueAsync(
        BackgroundTaskQueue queue,
        CancellationToken cancellationToken)
    {
        return GetLane(queue).DequeueAsync(cancellationToken);
    }

    public int GetCount(BackgroundTaskQueue queue)
    {
        return GetLane(queue).Count;
    }

    public DateTimeOffset? GetOldestEnqueuedAt(BackgroundTaskQueue queue)
    {
        return GetLane(queue).OldestEnqueuedAt;
    }

    public int GetHighWaterMark(BackgroundTaskQueue queue)
    {
        return GetLane(queue).HighWaterMark;
    }

    private FifoLane GetLane(BackgroundTaskQueue queue)
    {
        return _lanes.TryGetValue(queue, out var lane)
            ? lane
            : throw new ArgumentOutOfRangeException(nameof(queue));
    }

    private sealed class FifoLane
    {
        private readonly Channel<byte> _availableItems = CreateAvailabilityChannel();
        private readonly int _capacity;
        private readonly LinkedList<QueueEntry> _items = [];

        private readonly Dictionary<
            BackgroundTaskExecution,
            LinkedListNode<QueueEntry>> _nodes =
            new(ReferenceEqualityComparer.Instance);

        private readonly object _sync = new();
        private readonly TimeProvider _timeProvider;
        private int _highWaterMark;

        public FifoLane(
            int capacity,
            TimeProvider timeProvider)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _capacity = capacity;
            _timeProvider = timeProvider;
        }

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _items.Count;
                }
            }
        }

        public int HighWaterMark
        {
            get
            {
                lock (_sync)
                {
                    return _highWaterMark;
                }
            }
        }

        public DateTimeOffset? OldestEnqueuedAt
        {
            get
            {
                lock (_sync)
                {
                    return _items.First?.Value.EnqueuedAt;
                }
            }
        }

        public bool TryEnqueue(BackgroundTaskExecution execution)
        {
            lock (_sync)
            {
                // An enqueue already admitted by the engine can race shutdown.
                // Treat a terminal execution as accepted without storing it;
                // returning false would incorrectly run rejection accounting.
                if (execution.IsTerminal)
                    return true;

                if (_nodes.ContainsKey(execution))
                    return true;

                if (_items.Count >= _capacity)
                    return false;

                EnqueueCore(execution);
            }

            SignalAvailable();
            return true;
        }

        public bool EnqueueAccepted(BackgroundTaskExecution execution)
        {
            lock (_sync)
            {
                // The terminal finalizer uses the same lane lock in TryRemove.
                // Therefore either it removes this entry after the enqueue, or
                // this recheck observes the terminal state and no entry is added.
                if (execution.IsTerminal)
                    return false;

                if (_nodes.ContainsKey(execution))
                    return true;

                EnqueueCore(execution);
            }

            SignalAvailable();
            return true;
        }

        public bool TryRemove(BackgroundTaskExecution execution)
        {
            bool hasRemainingItems;

            lock (_sync)
            {
                if (!RemoveReference(execution))
                    return false;

                hasRemainingItems = _items.Count > 0;
            }

            if (hasRemainingItems)
                SignalAvailable();

            return true;
        }

        public async ValueTask<BackgroundTaskExecution> DequeueAsync(
            CancellationToken cancellationToken)
        {
            while (true)
            {
                BackgroundTaskExecution? execution;
                bool hasRemainingItems;

                lock (_sync)
                {
                    if (_items.First is { } first)
                    {
                        _items.RemoveFirst();
                        _nodes.Remove(first.Value.Execution);
                        execution = first.Value.Execution;
                    }
                    else
                    {
                        execution = null;
                    }

                    hasRemainingItems = _items.Count > 0;
                }

                if (execution is not null)
                {
                    if (hasRemainingItems)
                        SignalAvailable();

                    return execution;
                }

                await _availableItems.Reader.ReadAsync(cancellationToken);
            }
        }

        private void EnqueueCore(BackgroundTaskExecution execution)
        {
            var node = _items.AddLast(new QueueEntry(
                execution,
                _timeProvider.GetUtcNow()));
            _nodes.Add(execution, node);
            _highWaterMark = Math.Max(_highWaterMark, _items.Count);
        }

        private bool RemoveReference(BackgroundTaskExecution execution)
        {
            if (!_nodes.Remove(execution, out var node))
                return false;

            _items.Remove(node);
            return true;
        }

        private static Channel<byte> CreateAvailabilityChannel()
        {
            return Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = false,
                SingleWriter = false
            });
        }

        private void SignalAvailable()
        {
            _availableItems.Writer.TryWrite(0);
        }

        private readonly record struct QueueEntry(
            BackgroundTaskExecution Execution,
            DateTimeOffset EnqueuedAt);
    }
}