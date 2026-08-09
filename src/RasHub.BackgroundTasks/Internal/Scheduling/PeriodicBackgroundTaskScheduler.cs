using System.Collections.Concurrent;
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

    private readonly SemaphoreSlim _changed = new(0);
    private readonly IBackgroundTaskEngine _engine;
    private readonly ILogger<PeriodicBackgroundTaskScheduler> _logger;

    private readonly ConcurrentDictionary<string, ScheduleRegistration> _registrations =
        new(StringComparer.Ordinal);

    private readonly PriorityQueue<ScheduleRegistration, DateTimeOffset> _scheduled =
        new();

    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;

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
        var registration = new ScheduleRegistration(
            scheduleId,
            typeof(TTask),
            () => taskFactory(),
            effectiveOptions,
            interval,
            runImmediately ? now : now + interval);

        if (!_registrations.TryAdd(scheduleId, registration))
            throw new InvalidOperationException(
                $"Background task schedule '{scheduleId}' already exists.");

        EnqueueRegistration(registration);

        return new BackgroundTaskScheduleHandle(scheduleId, Remove);
    }

    public bool Remove(string scheduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);

        var removed = _registrations.TryRemove(scheduleId, out _);

        if (removed)
            _changed.Release();

        return removed;
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
                    CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

                var delayTask = Task.Delay(
                    delay,
                    _timeProvider,
                    waitCancellation.Token);

                var changedTask = _changed.WaitAsync(waitCancellation.Token);
                await Task.WhenAny(delayTask, changedTask);
                await waitCancellation.CancelAsync();
                continue;
            }

            lock (_sync)
            {
                _scheduled.Dequeue();
            }

            if (!_registrations.TryGetValue(registration.Id, out var active) ||
                !ReferenceEquals(active, registration))
                continue;

            registration.NextRunAt =
                _timeProvider.GetUtcNow() + registration.Interval;

            EnqueueRegistration(registration);
            RunScheduledTask(registration);
        }
    }

    private void RunScheduledTask(ScheduleRegistration registration)
    {
        try
        {
            var task = registration.TaskFactory();

            EnqueueRuntimeTask(task, registration.Options);
        }
        catch (BackgroundTaskRejectedException exception)
        {
            _logger.LogWarning(
                exception,
                "Scheduled background task {ScheduleId} was rejected",
                registration.Id);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Factory for background task schedule {ScheduleId} failed",
                registration.Id);
        }
    }

    private void EnqueueRuntimeTask(
        IBackgroundTask task,
        BackgroundTaskOptions options)
    {
        _engine.Enqueue(task, options);
    }

    private void EnqueueRegistration(ScheduleRegistration registration)
    {
        lock (_sync)
        {
            _scheduled.Enqueue(registration, registration.NextRunAt);
        }

        _changed.Release();
    }

    /// <summary>Stores one active schedule and its atomically updated next-run timestamp.</summary>
    private sealed class ScheduleRegistration(
        string id,
        Type taskType,
        Func<IBackgroundTask> taskFactory,
        BackgroundTaskOptions options,
        TimeSpan interval,
        DateTimeOffset nextRunAt)
    {
        private long _nextRunAtUtcTicks = nextRunAt.UtcTicks;

        public string Id { get; } = id;
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
    }
}