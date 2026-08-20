using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.Internal.Execution;

/// <summary>
///     Thread-safe mutable state machine for one task execution, including attempts, cancellation, and completion.
/// </summary>
internal sealed class BackgroundTaskExecution
{
    private const int MaximumLastErrorLength = 2_000;

    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource<BackgroundTaskResult> _completion;
    private readonly object _sync = new();
    private readonly Action<BackgroundTaskExecution> _terminalFinalizer;
    private int _attemptCount;
    private bool _cancellationRequested;
    private DateTimeOffset? _completedAt;
    private string? _lastError;
    private DateTimeOffset? _nextAttemptAt;
    private DateTimeOffset? _startedAt;

    private BackgroundTaskState _state;

    public BackgroundTaskExecution(
        IBackgroundTask backgroundTask,
        IBackgroundTaskInvoker invoker,
        BackgroundTaskOptions options,
        DateTimeOffset createdAt,
        Action<BackgroundTaskExecution> terminalFinalizer)
    {
        ArgumentNullException.ThrowIfNull(backgroundTask);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(terminalFinalizer);

        Id = Guid.NewGuid();
        BackgroundTask = backgroundTask;
        Invoker = invoker;
        Options = options;
        CreatedAt = createdAt;
        _terminalFinalizer = terminalFinalizer;
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

    public bool TrySucceed(
        DateTimeOffset completedAt,
        object? value = null)
    {
        BackgroundTaskResult result;

        lock (_sync)
        {
            if (_state != BackgroundTaskState.Running)
                return false;

            if (_cancellationRequested)
            {
                result = CompleteCanceled(completedAt);
            }
            else
            {
                _state = BackgroundTaskState.Succeeded;
                _completedAt = completedAt;
                _lastError = null;

                result = new BackgroundTaskResult(
                    Id,
                    BackgroundTaskOutcome.Succeeded,
                    _attemptCount,
                    null,
                    value);
            }
        }

        PublishTerminal(result);
        return true;
    }

    public CancellationRequest PrepareCancellation(DateTimeOffset requestedAt)
    {
        BackgroundTaskResult? result = null;

        lock (_sync)
        {
            if (IsTerminalState(_state))
                return default;

            if (_cancellationRequested)
                return default;

            _cancellationRequested = true;
            if (_state == BackgroundTaskState.Pending)
                result = CompleteCanceled(requestedAt);
        }

        return new CancellationRequest(true, result);
    }

    public Task SignalCancellationAsync(CancellationRequest request)
    {
        if (!request.IsAccepted)
            return Task.CompletedTask;

        return SignalCancellationCoreAsync(request);
    }

    private async Task SignalCancellationCoreAsync(CancellationRequest request)
    {
        // Cancellation callbacks are user code and must never run while the
        // execution state lock is held or synchronously block engine APIs.
        try
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            if (request.TerminalResult is not null)
                PublishTerminal(request.TerminalResult);
        }
    }

    public bool TryCancel(DateTimeOffset completedAt)
    {
        BackgroundTaskResult result;

        lock (_sync)
        {
            if (IsTerminalState(_state))
                return false;

            _cancellationRequested = true;
            result = CompleteCanceled(completedAt);
        }

        PublishTerminal(result);
        return true;
    }

    public bool TryScheduleRetry(
        Exception exception,
        DateTimeOffset nextAttemptAt)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var lastError = GetExceptionMessage(exception);

        lock (_sync)
        {
            if (_state != BackgroundTaskState.Running ||
                _cancellationRequested)
                return false;

            _state = BackgroundTaskState.Pending;
            _lastError = lastError;
            _nextAttemptAt = nextAttemptAt;
            return true;
        }
    }

    public bool TryFail(
        Exception exception,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var lastError = GetExceptionMessage(exception);

        BackgroundTaskResult result;

        lock (_sync)
        {
            if (IsTerminalState(_state))
                return false;

            if (_cancellationRequested)
            {
                result = CompleteCanceled(completedAt);
            }
            else
            {
                _state = BackgroundTaskState.Failed;
                _completedAt = completedAt;
                _lastError = lastError;

                result = new BackgroundTaskResult(
                    Id,
                    BackgroundTaskOutcome.Failed,
                    _attemptCount,
                    exception,
                    null);
            }
        }

        PublishTerminal(result);
        return true;
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
                _attemptCount,
                Options.MaxAttempts,
                CreatedAt,
                _startedAt,
                _completedAt,
                _nextAttemptAt,
                _cancellationRequested,
                _lastError,
                Options.DeduplicationKey,
                Options.ConcurrencyKey);
        }
    }

    private BackgroundTaskResult CompleteCanceled(DateTimeOffset completedAt)
    {
        _state = BackgroundTaskState.Canceled;
        _completedAt = completedAt;

        return new BackgroundTaskResult(
            Id,
            BackgroundTaskOutcome.Canceled,
            _attemptCount,
            null,
            null);
    }

    private void PublishTerminal(BackgroundTaskResult result)
    {
        try
        {
            _terminalFinalizer(this);
        }
        finally
        {
            // Caller-visible completion is published only after the engine
            // has released capacity and recorded the terminal execution.
            _completion.TrySetResult(result);
        }
    }

    private static string GetExceptionMessage(Exception exception)
    {
        string? message;

        try
        {
            message = exception.Message;
        }
        catch (Exception)
        {
            return exception.GetType().FullName ?? exception.GetType().Name;
        }

        if (string.IsNullOrEmpty(message))
            return exception.GetType().FullName ?? exception.GetType().Name;

        return message.Length <= MaximumLastErrorLength
            ? message
            : message[..MaximumLastErrorLength];
    }

    private static bool IsTerminalState(BackgroundTaskState state)
    {
        return state is
            BackgroundTaskState.Succeeded or
            BackgroundTaskState.Failed or
            BackgroundTaskState.Canceled;
    }

    public readonly record struct CancellationRequest(
        bool IsAccepted,
        BackgroundTaskResult? TerminalResult);
}
