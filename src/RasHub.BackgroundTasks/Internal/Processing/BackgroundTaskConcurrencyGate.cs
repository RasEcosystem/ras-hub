using RasHub.BackgroundTasks.Internal.Execution;
using RasHub.BackgroundTasks.Internal.Queues;

namespace RasHub.BackgroundTasks.Internal.Processing;

/// <summary>
///     Serializes executions by concurrency key and hands a released key to queued waiters in registration order.
/// </summary>
internal sealed class BackgroundTaskConcurrencyGate(
    IBackgroundTaskQueue queue)
{
    private readonly Dictionary<string, KeyState> _keys =
        new(StringComparer.Ordinal);

    private readonly object _sync = new();
    private int _waitingExecutionCount;

    /// <summary>Number of concurrency keys currently owned by running or granted executions.</summary>
    public int ActiveKeyCount
    {
        get
        {
            lock (_sync)
            {
                return _keys.Count;
            }
        }
    }

    /// <summary>Number of executions waiting behind the owner of a concurrency key.</summary>
    public int WaitingExecutionCount
    {
        get
        {
            lock (_sync)
            {
                return _waitingExecutionCount;
            }
        }
    }

    /// <summary>
    ///     Acquires an unkeyed execution immediately, acquires a free key, or retains the execution as a FIFO waiter.
    /// </summary>
    public bool TryAcquireOrWait(
        BackgroundTaskExecution execution,
        out IDisposable? lease)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var key = execution.Options.ConcurrencyKey;
        if (key is null)
        {
            lease = null;
            return true;
        }

        lock (_sync)
        {
            if (execution.IsTerminal)
            {
                lease = null;
                return false;
            }

            if (!_keys.TryGetValue(key, out var state))
            {
                state = new KeyState(execution, OwnerPhase.Running);
                _keys.Add(key, state);
                lease = CreateLease(key, state);
                return true;
            }

            if (ReferenceEquals(state.Owner, execution))
            {
                if (state.Phase == OwnerPhase.Granted)
                {
                    state.Phase = OwnerPhase.Running;
                    lease = CreateLease(key, state);
                    return true;
                }

                // A duplicate lane entry for the current owner is discarded.
                // The already-running attempt still owns the key and will
                // release it through its original lease.
                lease = null;
                return false;
            }

            if (!state.WaiterNodes.ContainsKey(execution))
            {
                var node = state.Waiters.AddLast(execution);
                state.WaiterNodes.Add(execution, node);
                _waitingExecutionCount++;
            }

            lease = null;
            return false;
        }
    }

    /// <summary>
    ///     Physically removes a terminal execution and immediately hands its key to the next live waiter.
    /// </summary>
    public bool TryRemove(BackgroundTaskExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var key = execution.Options.ConcurrencyKey;
        if (key is null)
            return false;

        GrantedExecution? granted = null;
        bool removed;

        lock (_sync)
        {
            if (!_keys.TryGetValue(key, out var state))
                return false;

            if (ReferenceEquals(state.Owner, execution))
            {
                removed = true;
                granted = GrantNextOrRemoveKey(key, state);
            }
            else if (state.WaiterNodes.Remove(execution, out var node))
            {
                state.Waiters.Remove(node);
                _waitingExecutionCount--;
                removed = true;
            }
            else
            {
                removed = false;
            }
        }

        if (granted is not null)
            DispatchGranted(granted.Value);

        return removed;
    }

    private Lease CreateLease(
        string key,
        KeyState state)
    {
        return new Lease(
            this,
            key,
            state,
            state.Owner,
            state.Generation);
    }

    private void Release(
        string key,
        KeyState state,
        BackgroundTaskExecution owner,
        long generation)
    {
        GrantedExecution? granted = null;

        lock (_sync)
        {
            if (!_keys.TryGetValue(key, out var active) ||
                !ReferenceEquals(active, state) ||
                !ReferenceEquals(state.Owner, owner) ||
                state.Generation != generation ||
                state.Phase != OwnerPhase.Running)
                return;

            granted = GrantNextOrRemoveKey(key, state);
        }

        if (granted is not null)
            DispatchGranted(granted.Value);
    }

    private GrantedExecution? GrantNextOrRemoveKey(
        string key,
        KeyState state)
    {
        while (state.Waiters.First is { } node)
        {
            state.Waiters.RemoveFirst();
            state.WaiterNodes.Remove(node.Value);
            _waitingExecutionCount--;

            if (node.Value.IsTerminal)
                continue;

            state.Owner = node.Value;
            state.Phase = OwnerPhase.Granted;
            state.Generation++;

            return new GrantedExecution(
                key,
                state,
                node.Value,
                state.Generation);
        }

        _keys.Remove(key);
        return null;
    }

    private void DispatchGranted(GrantedExecution granted)
    {
        var current = granted;

        while (true)
        {
            if (queue.EnqueueAccepted(current.Execution))
                return;

            GrantedExecution? next = null;

            lock (_sync)
            {
                if (_keys.TryGetValue(current.Key, out var active) &&
                    ReferenceEquals(active, current.State) &&
                    ReferenceEquals(active.Owner, current.Execution) &&
                    active.Generation == current.Generation &&
                    active.Phase == OwnerPhase.Granted)
                    next = GrantNextOrRemoveKey(current.Key, active);
            }

            if (next is null)
                return;

            current = next.Value;
        }
    }

    private enum OwnerPhase
    {
        Running,
        Granted
    }

    private sealed class KeyState(
        BackgroundTaskExecution owner,
        OwnerPhase phase)
    {
        public long Generation { get; set; } = 1;

        public BackgroundTaskExecution Owner { get; set; } = owner;

        public OwnerPhase Phase { get; set; } = phase;

        public LinkedList<BackgroundTaskExecution> Waiters { get; } = [];

        public Dictionary<BackgroundTaskExecution, LinkedListNode<BackgroundTaskExecution>> WaiterNodes { get; } =
            new(ReferenceEqualityComparer.Instance);
    }

    private readonly record struct GrantedExecution(
        string Key,
        KeyState State,
        BackgroundTaskExecution Execution,
        long Generation);

    /// <summary>Releases only the exact owner generation for which this lease was issued.</summary>
    private sealed class Lease(
        BackgroundTaskConcurrencyGate gate,
        string key,
        KeyState state,
        BackgroundTaskExecution owner,
        long generation) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                gate.Release(key, state, owner, generation);
        }
    }
}
