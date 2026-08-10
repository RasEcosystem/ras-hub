using RasHub.BackgroundTasks.Configuration;
using RasHub.BackgroundTasks.Internal.Execution;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.Internal.Queues;

internal sealed class InMemoryBackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly IReadOnlyDictionary<BackgroundTaskQueue, FifoLane> _lanes;

    public InMemoryBackgroundTaskQueue(BackgroundTaskEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _lanes = new Dictionary<BackgroundTaskQueue, FifoLane>
        {
            [BackgroundTaskQueue.Interactive] = new(options.InteractiveQueueCapacity),
            [BackgroundTaskQueue.Synchronization] = new(options.SynchronizationQueueCapacity),
            [BackgroundTaskQueue.Maintenance] = new(options.MaintenanceQueueCapacity)
        };
    }

    public bool TryEnqueue(BackgroundTaskExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        return GetLane(execution.Options.Queue).TryEnqueue(execution);
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

    private FifoLane GetLane(BackgroundTaskQueue queue)
    {
        return _lanes.TryGetValue(queue, out var lane)
            ? lane
            : throw new ArgumentOutOfRangeException(nameof(queue));
    }

    private sealed class FifoLane
    {
        private readonly SemaphoreSlim _availableItems = new(0);
        private readonly int _capacity;
        private readonly Queue<BackgroundTaskExecution> _items = new();
        private readonly object _sync = new();
        private int _highWaterMark;

        public FifoLane(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _capacity = capacity;
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

        public bool TryEnqueue(BackgroundTaskExecution execution)
        {
            lock (_sync)
            {
                if (_items.Count >= _capacity)
                    return false;

                _items.Enqueue(execution);
                _highWaterMark = Math.Max(_highWaterMark, _items.Count);
            }

            _availableItems.Release();
            return true;
        }

        public async ValueTask<BackgroundTaskExecution> DequeueAsync(
            CancellationToken cancellationToken)
        {
            await _availableItems.WaitAsync(cancellationToken);

            lock (_sync)
            {
                return _items.Dequeue();
            }
        }
    }
}