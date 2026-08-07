using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Models;

namespace RasHub.Synchronization.Internal.Execution;

/// <summary>
///     Thread-safe mutable state machine for one task execution, including attempts, cancellation, and completion.
/// </summary>
internal sealed class BackgroundTaskExecution
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource<BackgroundTaskResult> _completion;
    private readonly object _sync = new();
    private int _attemptCount;
    private bool _cancellationRequested;
    private DateTimeOffset? _completedAt;
    private Exception? _lastException;
    private DateTimeOffset? _nextAttemptAt;
    private DateTimeOffset? _startedAt;

    private BackgroundTaskState _state;

    public BackgroundTaskExecution(
        IBackgroundTask backgroundTask,
        IBackgroundTaskInvoker invoker,
        BackgroundTaskOptions options,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(backgroundTask);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(options);

        Id = Guid.NewGuid();
        BackgroundTask = backgroundTask;
        Invoker = invoker;
        Options = options;
        CreatedAt = createdAt;
        _state = BackgroundTaskState.Pending;

        _completion = new TaskCompletionSource<BackgroundTaskResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Guid Id { get; }

    public IBackgroundTask BackgroundTask { get; }

    public IBackgroundTaskInvoker Invoker { get; }

    public BackgroundTaskOptions Options { get; }

    public DateTimeOffset CreatedAt { get; }

    public CancellationToken CancellationToken =>
        _cancellation.Token;

    internal Task<BackgroundTaskResult> Completion =>
        _completion.Task;

    public BackgroundTaskState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public int AttemptCount
    {
        get
        {
            lock (_sync)
            {
                return _attemptCount;
            }
        }
    }

    public bool IsTerminal
    {
        get
        {
            lock (_sync)
            {
                return IsTerminalState(_state);
            }
        }
    }

    public BackgroundTaskHandle CreateHandle()
    {
        return new BackgroundTaskHandle(
            Id,
            _completion.Task);
    }

    public bool TryStart(DateTimeOffset startedAt)
    {
        lock (_sync)
        {
            if (_state != BackgroundTaskState.Pending ||
                _cancellationRequested)
                return false;

            _state = BackgroundTaskState.Running;
            _attemptCount++;
            _startedAt ??= startedAt;
            _nextAttemptAt = null;
            return true;
        }
    }

    public bool TrySucceed(DateTimeOffset completedAt)
    {
        lock (_sync)
        {
            if (_state != BackgroundTaskState.Running)
                return false;

            if (_cancellationRequested)
                return CompleteCanceled(completedAt);

            _state = BackgroundTaskState.Succeeded;
            _completedAt = completedAt;

            return _completion.TrySetResult(
                new BackgroundTaskResult(
                    Id,
                    BackgroundTaskOutcome.Succeeded,
                    _attemptCount,
                    null));
        }
    }

    public bool RequestCancellation(DateTimeOffset requestedAt)
    {
        lock (_sync)
        {
            if (IsTerminalState(_state))
                return false;

            _cancellationRequested = true;
            if (_state == BackgroundTaskState.Pending)
                CompleteCanceled(requestedAt);
        }

        // Cancellation callbacks are user code and must never run while the
        // execution state lock is held.
        _cancellation.Cancel();

        return true;
    }

    public bool TryCancel(DateTimeOffset completedAt)
    {
        lock (_sync)
        {
            if (IsTerminalState(_state))
                return false;

            return CompleteCanceled(completedAt);
        }
    }

    public bool TryScheduleRetry(
        Exception exception,
        DateTimeOffset nextAttemptAt)
    {
        ArgumentNullException.ThrowIfNull(exception);

        lock (_sync)
        {
            if (_state != BackgroundTaskState.Running ||
                _cancellationRequested)
                return false;

            _state = BackgroundTaskState.Pending;
            _lastException = exception;
            _nextAttemptAt = nextAttemptAt;
            return true;
        }
    }

    public bool TryFail(
        Exception exception,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(exception);

        lock (_sync)
        {
            if (_state != BackgroundTaskState.Running)
                return false;

            _state = BackgroundTaskState.Failed;
            _completedAt = completedAt;
            _lastException = exception;

            return _completion.TrySetResult(
                new BackgroundTaskResult(
                    Id,
                    BackgroundTaskOutcome.Failed,
                    _attemptCount,
                    exception));
        }
    }

    public BackgroundTaskSnapshot CreateSnapshot()
    {
        lock (_sync)
        {
            return new BackgroundTaskSnapshot(
                Id,
                BackgroundTask.GetType(),
                _state,
                Options.Queue,
                Options.Priority,
                _attemptCount,
                Options.MaxAttempts,
                CreatedAt,
                _startedAt,
                _completedAt,
                _nextAttemptAt,
                _cancellationRequested,
                _lastException?.Message,
                Options.DeduplicationKey,
                Options.ConcurrencyKey);
        }
    }

    private bool CompleteCanceled(DateTimeOffset completedAt)
    {
        _state = BackgroundTaskState.Canceled;
        _completedAt = completedAt;

        return _completion.TrySetResult(
            new BackgroundTaskResult(
                Id,
                BackgroundTaskOutcome.Canceled,
                _attemptCount,
                null));
    }

    private static bool IsTerminalState(BackgroundTaskState state)
    {
        return state is
            BackgroundTaskState.Succeeded or
            BackgroundTaskState.Failed or
            BackgroundTaskState.Canceled;
    }
}