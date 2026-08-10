namespace RasHub.BackgroundTasks.Internal.Diagnostics;

/// <summary>Tracks the lifecycle and live process count of the in-process engine.</summary>
internal sealed class BackgroundTaskRuntimeState(TimeProvider timeProvider)
{
    private readonly HashSet<string> _liveProcesses = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private int _expectedProcessCount;
    private DateTimeOffset? _faultedAt;
    private string? _faultedProcess;
    private BackgroundTaskRuntimeStatus _status = BackgroundTaskRuntimeStatus.NotStarted;

    public bool TryInitialize(int expectedProcessCount)
    {
        if (expectedProcessCount < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedProcessCount));

        lock (_sync)
        {
            if (_status is
                BackgroundTaskRuntimeStatus.Stopping or
                BackgroundTaskRuntimeStatus.Stopped or
                BackgroundTaskRuntimeStatus.Faulted)
                return false;

            if (_status != BackgroundTaskRuntimeStatus.NotStarted)
                throw new InvalidOperationException(
                    "The background task runtime has already been initialized.");

            _expectedProcessCount = expectedProcessCount;
            _status = BackgroundTaskRuntimeStatus.Starting;
            return true;
        }
    }

    public void ProcessStarted(string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);

        lock (_sync)
        {
            if (!_liveProcesses.Add(processName))
                throw new InvalidOperationException(
                    $"Background task process '{processName}' is already running.");
        }
    }

    public void ProcessStopped(string processName)
    {
        lock (_sync)
        {
            _liveProcesses.Remove(processName);
        }
    }

    public void MarkRunning()
    {
        lock (_sync)
        {
            if (_status != BackgroundTaskRuntimeStatus.Starting)
                return;

            if (_liveProcesses.Count == _expectedProcessCount)
            {
                _status = BackgroundTaskRuntimeStatus.Running;
                return;
            }

            MarkFaultedCore("startup");
        }
    }

    public void MarkStopping()
    {
        lock (_sync)
        {
            if (_status is not BackgroundTaskRuntimeStatus.Faulted and
                not BackgroundTaskRuntimeStatus.Stopped)
                _status = BackgroundTaskRuntimeStatus.Stopping;
        }
    }

    public void MarkStopped()
    {
        lock (_sync)
        {
            if (_status != BackgroundTaskRuntimeStatus.Faulted)
                _status = BackgroundTaskRuntimeStatus.Stopped;
        }
    }

    public void MarkFaulted(string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);

        lock (_sync)
        {
            MarkFaultedCore(processName);
        }
    }

    public BackgroundTaskRuntimeSnapshot CreateSnapshot()
    {
        lock (_sync)
        {
            return new BackgroundTaskRuntimeSnapshot(
                _status,
                _expectedProcessCount,
                _liveProcesses.Count,
                _faultedProcess,
                _faultedAt);
        }
    }

    private void MarkFaultedCore(string processName)
    {
        _status = BackgroundTaskRuntimeStatus.Faulted;
        _faultedProcess ??= processName;
        _faultedAt ??= timeProvider.GetUtcNow();
    }
}

internal enum BackgroundTaskRuntimeStatus
{
    NotStarted,
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted
}

internal sealed record BackgroundTaskRuntimeSnapshot(
    BackgroundTaskRuntimeStatus Status,
    int ExpectedProcessCount,
    int LiveProcessCount,
    string? FaultedProcess,
    DateTimeOffset? FaultedAt);
