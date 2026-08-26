using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.BackgroundTasks.Internal.Execution;
using RasHub.BackgroundTasks.Models;

namespace RasHub.BackgroundTasks.Internal.Scheduling;

/// <summary>
///     Maintains in-memory periodic registrations and enqueues one task whenever a schedule becomes due.
/// </summary>
internal sealed class PeriodicBackgroundTaskScheduler
    : IBackgroundTaskScheduler
{
    private static readonly TimeSpan MaximumTimerSlice =
        TimeSpan.FromDays(1);

    private readonly Channel<byte> _changed = CreateChangeChannel();
    private readonly IBackgroundTaskEngine _engine;
    private readonly ILogger<PeriodicBackgroundTaskScheduler> _logger;

    private readonly ConcurrentDictionary<string, ScheduleRegistration> _registrations =
        new(StringComparer.Ordinal);

    private readonly PriorityQueue<ScheduleRegistration, DateTimeOffset> _scheduled =
        new();

    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private bool _accepting = true;
    private long _nextRegistrationGeneration;

    public PeriodicBackgroundTaskScheduler(
        IBackgroundTaskEngine engine,
        TimeProvider timeProvider,
        ILogger<PeriodicBackgroundTaskScheduler> logger)
    {
        _engine = engine;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public BackgroundTaskScheduleHandle Schedule<TTask>(
        string scheduleId,
        Func<TTask> taskFactory,
        TimeSpan interval,
        BackgroundTaskOptions? taskOptions = null,
        bool runImmediately = false)
        where TTask : IBackgroundTask
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        ArgumentNullException.ThrowIfNull(taskFactory);

        if (scheduleId.Length > 200)
            throw new ArgumentException("Schedule ID is too long.", nameof(scheduleId));

        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));

        taskOptions ??= new BackgroundTaskOptions();
        BackgroundTaskOptionsValidator.Validate(taskOptions);

        var effectiveOptions = taskOptions.DeduplicationKey is null
            ? taskOptions with { DeduplicationKey = $"schedule:{scheduleId}" }
            : taskOptions;

        var now = _timeProvider.GetUtcNow();

        if (!TryAddInterval(now, interval, out var firstRegularRunAt))
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                "The interval places the next run outside the supported UTC range.");

        var registrationGeneration =
            Interlocked.Increment(ref _nextRegistrationGeneration);
        var registration = new ScheduleRegistration(
            scheduleId,
            registrationGeneration,
            typeof(TTask),
            () => taskFactory(),
            effectiveOptions,
            interval,
            runImmediately ? now : firstRegularRunAt);

        lock (_sync)
        {
            if (!_accepting)
                throw new BackgroundTaskRejectedException(
                    typeof(TTask),
                    "the background task scheduler is stopping");

            if (!_registrations.TryAdd(scheduleId, registration))
                throw new InvalidOperationException(
                    $"Background task schedule '{scheduleId}' already exists.");

            _scheduled.Enqueue(registration, registration.NextRunAt);
        }

        SignalChanged();
        var handleRemoval = new ScheduleHandleRemoval(
            this,
            registrationGeneration);

        return new BackgroundTaskScheduleHandle(
            scheduleId,
            handleRemoval.Remove);
    }

    public bool Remove(string scheduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);

        if (!_registrations.TryRemove(scheduleId, out var registration))
            return false;

        CompleteRemoval(registration);
        return true;
    }

    public IReadOnlyList<BackgroundTaskScheduleSnapshot> GetSchedules()
    {
        return _registrations.Values
            .Select(registration => new BackgroundTaskScheduleSnapshot(
                registration.Id,
                registration.TaskType,
                registration.Interval,
                registration.NextRunAt))
            .OrderBy(snapshot => snapshot.NextRunAt)
            .ToArray();
    }

    /// <summary>
    ///     Atomically closes schedule admission and drops all retained registrations without waiting for user factories.
    /// </summary>
    public void StopAcceptingAndClear()
    {
        lock (_sync)
        {
            if (!_accepting)
                return;

            _accepting = false;

            foreach (var registration in _registrations.Values)
                registration.MarkRemoved();

            _registrations.Clear();
            _scheduled.Clear();
        }

        SignalChanged();
    }

    private bool Remove(
        string scheduleId,
        long generation)
    {
        if (!_registrations.TryGetValue(scheduleId, out var registration) ||
            registration.Generation != generation ||
            !_registrations.TryRemove(
                new KeyValuePair<string, ScheduleRegistration>(
                    scheduleId,
                    registration)))
            return false;

        CompleteRemoval(registration);
        return true;
    }

    private void CompleteRemoval(ScheduleRegistration registration)
    {
        // A successful Remove is a synchronization boundary: if a dispatch
        // already owns this registration, wait for it to finish. Otherwise
        // mark it removed before it can start.
        lock (registration.DispatchSync)
        {
            registration.MarkRemoved();

            lock (_sync)
            {
                while (_scheduled.Remove(
                           registration,
                           out _,
                           out _))
                {
                }
            }
        }

        SignalChanged();
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ScheduleRegistration? registration;
            DateTimeOffset dueAt;

            lock (_sync)
            {
                if (!_scheduled.TryPeek(out registration, out dueAt))
                    registration = null;
            }

            if (registration is null)
            {
                await _changed.Reader.ReadAsync(stoppingToken);
                continue;
            }

            var delay = dueAt - _timeProvider.GetUtcNow();

            if (delay > TimeSpan.Zero)
            {
                delay = delay > MaximumTimerSlice
                    ? MaximumTimerSlice
                    : delay;

                using var waitCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

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
                    // The losing wait must finish before the loop installs its
                    // next waiter, otherwise it can consume a later signal.
                }

                continue;
            }

            lock (_sync)
            {
                _scheduled.TryDequeue(out registration, out dueAt);
            }

            if (registration is null)
                continue;

            var now = _timeProvider.GetUtcNow();

            // GetUtcNow is deliberately not assumed to be monotonic.
            if (dueAt > now)
            {
                TryEnqueueRegistration(registration, dueAt);
                continue;
            }

            lock (registration.DispatchSync)
            {
                if (registration.IsRemoved ||
                    !_registrations.TryGetValue(registration.Id, out var active) ||
                    !ReferenceEquals(active, registration))
                    continue;

                if (TryAddInterval(
                        now,
                        registration.Interval,
                        out var nextRunAt))
                {
                    registration.NextRunAt = nextRunAt;
                    if (!TryEnqueueRegistration(registration, nextRunAt))
                        continue;
                }
                else
                {
                    registration.MarkRemoved();
                    _registrations.TryRemove(
                        new KeyValuePair<string, ScheduleRegistration>(
                            registration.Id,
                            registration));

                    _logger.LogWarning(
                        "Background task schedule {ScheduleId} reached the supported UTC range and was removed",
                        registration.Id);
                }

                if (!Volatile.Read(ref _accepting))
                    continue;

                RunScheduledTask(registration);
            }
        }
    }

    private void RunScheduledTask(ScheduleRegistration registration)
    {
        try
        {
            var task = registration.TaskFactory();

            EnqueueRuntimeTask(task, registration.Options);
        }
        catch (BackgroundTaskRejectedException)
        {
            _logger.LogWarning(
                "Scheduled background task {ScheduleId} of type {TaskType} was rejected",
                registration.Id,
                registration.TaskType.FullName);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Factory for background task schedule {ScheduleId} of type {TaskType} failed with {FailureType}",
                registration.Id,
                registration.TaskType.FullName,
                exception.GetType().FullName);
        }
    }

    private void EnqueueRuntimeTask(
        IBackgroundTask task,
        BackgroundTaskOptions options)
    {
        _engine.Enqueue(task, options);
    }

    private bool TryEnqueueRegistration(
        ScheduleRegistration registration,
        DateTimeOffset dueAt)
    {
        lock (registration.DispatchSync)
        {
            if (registration.IsRemoved)
                return false;

            lock (_sync)
            {
                if (!_accepting ||
                    registration.IsRemoved ||
                    !_registrations.TryGetValue(
                        registration.Id,
                        out var active) ||
                    !ReferenceEquals(active, registration))
                    return false;

                _scheduled.Enqueue(registration, dueAt);
            }
        }

        SignalChanged();
        return true;
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

    private static bool TryAddInterval(
        DateTimeOffset timestamp,
        TimeSpan interval,
        out DateTimeOffset result)
    {
        try
        {
            result = timestamp + interval;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            result = default;
            return false;
        }
    }

    /// <summary>Stores one active schedule and its atomically updated next-run timestamp.</summary>
    private sealed class ScheduleRegistration(
        string id,
        long generation,
        Type taskType,
        Func<IBackgroundTask> taskFactory,
        BackgroundTaskOptions options,
        TimeSpan interval,
        DateTimeOffset nextRunAt)
    {
        private int _isRemoved;
        private long _nextRunAtUtcTicks = nextRunAt.UtcTicks;

        public object DispatchSync { get; } = new();
        public string Id { get; } = id;
        public long Generation { get; } = generation;

        public bool IsRemoved => Volatile.Read(ref _isRemoved) != 0;
        public Type TaskType { get; } = taskType;
        public Func<IBackgroundTask> TaskFactory { get; } = taskFactory;
        public BackgroundTaskOptions Options { get; } = options;
        public TimeSpan Interval { get; } = interval;

        public DateTimeOffset NextRunAt
        {
            get => new(
                Interlocked.Read(ref _nextRunAtUtcTicks),
                TimeSpan.Zero);
            set => Interlocked.Exchange(
                ref _nextRunAtUtcTicks,
                value.UtcTicks);
        }

        public void MarkRemoved()
        {
            Interlocked.Exchange(ref _isRemoved, 1);
        }
    }

    /// <summary>Keeps a schedule handle independent from the registration's task factory closure.</summary>
    private sealed class ScheduleHandleRemoval(
        PeriodicBackgroundTaskScheduler scheduler,
        long generation)
    {
        public bool Remove(string scheduleId)
        {
            return scheduler.Remove(scheduleId, generation);
        }
    }
}
