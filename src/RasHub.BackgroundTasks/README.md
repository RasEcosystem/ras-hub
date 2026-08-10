# RasHub Background Tasks

`RasHub.BackgroundTasks` runs in-process work. Feature projects define task messages and scoped handlers; this module
owns execution mechanics.

## Behavior

- bounded FIFO lanes for `Interactive`, `Synchronization`, and `Maintenance`;
- dedicated workers per lane;
- retry with exponential backoff and per-attempt timeout;
- cancellation, active-task deduplication, and concurrency keys;
- periodic scheduling;
- task snapshots, retained history, logging, .NET metrics, and a readiness health check.

Queues and schedules are not persistent. A restart discards pending work, so business-critical operations must be safely
repeatable from persisted state.

## Define and enqueue work

```csharp
public sealed record SynchronizeGateTask(Guid GateId) : IBackgroundTask;

public sealed class SynchronizeGateTaskHandler
    : IBackgroundTaskHandler<SynchronizeGateTask>
{
    public Task ExecuteAsync(
        SynchronizeGateTask task,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

var handle = engine.Enqueue(
    new SynchronizeGateTask(gateId),
    new BackgroundTaskOptions
    {
        Queue = BackgroundTaskQueue.Interactive,
        MaxAttempts = 2,
        Timeout = TimeSpan.FromSeconds(30),
        DeduplicationKey = $"gate-sync:{gateId}",
        ConcurrencyKey = $"gate:{gateId}"
    });

var result = await handle.WaitAsync(requestCancellationToken);
```

Each attempt gets a new DI scope. Canceling `WaitAsync` stops only the caller's wait; use `engine.Cancel(handle.Id)` to
request task cancellation.

## Periodic work

```csharp
using var schedule = scheduler.Schedule(
    $"gate-sync:{gateId}",
    () => new SynchronizeGateTask(gateId),
    TimeSpan.FromMinutes(1),
    new BackgroundTaskOptions
    {
        Queue = BackgroundTaskQueue.Synchronization,
        ConcurrencyKey = $"gate:{gateId}"
    },
    runImmediately: true);
```

The scheduler supplies a deduplication key when none is provided. Overlapping occurrences share the active execution,
and missed intervals are not replayed.

## Failure and monitoring

Exceptions retry until `MaxAttempts` is reached. Throw
`NonRetryableBackgroundTaskException` for permanent failures. Timeout and cancellation remain cooperative.

The `BackgroundTasks` configuration section controls lane capacities and worker counts, retention, cleanup, and
active/history limits. The meter name is
`RasHub.BackgroundTasks`; the readiness check is `background-tasks`.
