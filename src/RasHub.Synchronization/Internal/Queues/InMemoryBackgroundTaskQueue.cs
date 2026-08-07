using RasHub.Synchronization.Configuration;
using RasHub.Synchronization.Internal.Execution;
using RasHub.Synchronization.Models;

namespace RasHub.Synchronization.Internal.Queues;

/// <summary>
///     Stores executions in three bounded in-memory lanes with priority ordering and periodic FIFO fairness.
/// </summary>
internal sealed class InMemoryBackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly IReadOnlyDictionary<BackgroundTaskQueue, PriorityLane> _lanes;

    public InMemoryBackgroundTaskQueue(SynchronizationEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _lanes = new Dictionary<BackgroundTaskQueue, PriorityLane>
        {
            [BackgroundTaskQueue.Interactive] = new(
                options.InteractiveQueueCapacity,
                options.PriorityFairnessInterval),
            [BackgroundTaskQueue.Synchronization] = new(
                options.QueueCapacity,
                options.PriorityFairnessInterval),
            [BackgroundTaskQueue.Maintenance] = new(
                options.MaintenanceQueueCapacity,
                options.PriorityFairnessInterval)
        };
    }

    public bool TryEnqueue(BackgroundTaskExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        return GetLane(execution.Options.Queue)
            .TryEnqueue(execution);
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

    public int GetHighWaterMark(BackgroundTaskQueue queue)
    {
        return GetLane(queue).HighWaterMark;
    }

    private PriorityLane GetLane(BackgroundTaskQueue queue)
    {
        return _lanes.TryGetValue(queue, out var lane)
            ? lane
            : throw new ArgumentOutOfRangeException(nameof(queue));
    }

    /// <summary>Owns the capacity, ordering, and waiter signal for one isolated queue lane.</summary>
    private sealed class PriorityLane
    {
        private readonly Dictionary<long, QueueEntry> _available = [];
        private readonly int _capacity;
        private readonly int _fairnessInterval;

        private readonly Queue<QueueEntry> _fifo = new();
        private readonly SemaphoreSlim _items = new(0);

        private readonly PriorityQueue<QueueEntry, QueuePriority> _priority =
            new(new QueuePriorityComparer());

        private readonly object _sync = new();
        private long _dequeueCount;

        private int _highWaterMark;

        private long _sequence;

        public PriorityLane(int capacity, int fairnessInterval)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            if (fairnessInterval < 1)
                throw new ArgumentOutOfRangeException(nameof(fairnessInterval));

            _capacity = capacity;
            _fairnessInterval = fairnessInterval;
        }

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _available.Count;
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

        public bool TryEnqueue(BackgroundTaskExecution execution)
        {
            lock (_sync)
            {
                if (_available.Count >= _capacity)
                    return false;

                var sequence = ++_sequence;
                var entry = new QueueEntry(sequence, execution);

                _available.Add(sequence, entry);
                _highWaterMark = Math.Max(_highWaterMark, _available.Count);
                _fifo.Enqueue(entry);
                _priority.Enqueue(
                    entry,
                    new QueuePriority(execution.Options.Priority, sequence));
            }

            _items.Release();
            return true;
        }

        public async ValueTask<BackgroundTaskExecution> DequeueAsync(
            CancellationToken cancellationToken)
        {
            await _items.WaitAsync(cancellationToken);

            lock (_sync)
            {
                _dequeueCount++;

                var useFifo =
                    _dequeueCount % _fairnessInterval == 0;

                var entry = useFifo
                    ? DequeueFifo()
                    : DequeuePriority();

                _available.Remove(entry.Sequence);
                CompactIfNeeded();
                return entry.Execution;
            }
        }

        private QueueEntry DequeueFifo()
        {
            while (_fifo.Count > 0)
            {
                var entry = _fifo.Dequeue();

                if (_available.ContainsKey(entry.Sequence))
                    return entry;
            }

            throw new InvalidOperationException("Queue semaphore is inconsistent.");
        }

        private QueueEntry DequeuePriority()
        {
            while (_priority.Count > 0)
            {
                var entry = _priority.Dequeue();

                if (_available.ContainsKey(entry.Sequence))
                    return entry;
            }

            throw new InvalidOperationException("Queue semaphore is inconsistent.");
        }

        private void CompactIfNeeded()
        {
            var maximumInternalSize =
                Math.Max(32, _available.Count * 2);

            if (_fifo.Count <= maximumInternalSize &&
                _priority.Count <= maximumInternalSize)
                return;

            var activeEntries = _available.Values
                .OrderBy(entry => entry.Sequence)
                .ToArray();

            _fifo.Clear();
            _priority.Clear();

            foreach (var entry in activeEntries)
            {
                _fifo.Enqueue(entry);
                _priority.Enqueue(
                    entry,
                    new QueuePriority(
                        entry.Execution.Options.Priority,
                        entry.Sequence));
            }
        }

        /// <summary>Pairs an execution with its monotonically increasing enqueue order.</summary>
        private sealed record QueueEntry(
            long Sequence,
            BackgroundTaskExecution Execution);

        /// <summary>Combines business priority with FIFO order for deterministic selection.</summary>
        private readonly record struct QueuePriority(
            int Priority,
            long Sequence);

        /// <summary>Selects higher priorities first and preserves FIFO order for equal priorities.</summary>
        private sealed class QueuePriorityComparer
            : IComparer<QueuePriority>
        {
            public int Compare(QueuePriority x, QueuePriority y)
            {
                var priorityComparison =
                    y.Priority.CompareTo(x.Priority);

                return priorityComparison != 0
                    ? priorityComparison
                    : x.Sequence.CompareTo(y.Sequence);
            }
        }
    }
}
