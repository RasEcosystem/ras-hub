# RasHub Synchronization Engine

`RasHub.Synchronization` is the in-process dispatcher for long-running and periodic RasHub work. The module owns
execution mechanics; feature modules own task messages and handlers.

## Guarantees

- bounded in-memory queues with explicit overload rejection;
- isolated worker quotas for interactive, synchronization, and maintenance work;
- priority ordering with periodic FIFO selection to prevent starvation;
- atomic execution state transitions and observable task snapshots;
- cooperative cancellation and per-attempt timeout;
- retry with exponential backoff and a configurable maximum delay;
- active-task deduplication;
- serialization by a concurrency key, such as `gate:{id}`;
- periodic schedules without overlapping copies by default;
- graceful host shutdown;
- startup recovery extension points;
- structured logging, .NET metrics, and a readiness health check.

The queue is intentionally not persistent. A process restart loses queued work. Critical work must therefore be
reconstructible from durable business state and restored through `IBackgroundTaskRecoverySource`.

## Declaring work

```csharp
public sealed record SynchronizeRasGateTask(Guid RasGateId)
    : IBackgroundTask;

public sealed class SynchronizeRasGateTaskHandler
    : IBackgroundTaskHandler<SynchronizeRasGateTask>
{
    public Task ExecuteAsync(
        SynchronizeRasGateTask task,
        CancellationToken cancellationToken)
    {
        // Fetch, validate, and atomically publish a snapshot.
        return Task.CompletedTask;
    }
}
```

Register handlers with their natural DI lifetime. A new DI scope is created for every attempt, so scoped handlers can
safely use `DbContext`.

## Enqueuing and waiting

```csharp
var handle = engine.Enqueue(
    new SynchronizeRasGateTask(gateId),
    new BackgroundTaskOptions
    {
        Queue = BackgroundTaskQueue.Interactive,
        Priority = 100,
        MaxAttempts = 5,
        RetryDelay = TimeSpan.FromSeconds(1),
        RetryBackoffFactor = 2,
        MaxRetryDelay = TimeSpan.FromMinutes(1),
        Timeout = TimeSpan.FromSeconds(30),
        DeduplicationKey = $"gate-sync:{gateId}",
        ConcurrencyKey = $"gate:{gateId}"
    });

var result = await handle.WaitAsync(requestCancellationToken);
```

Canceling `WaitAsync` only stops the caller's wait. Use
`engine.Cancel(handle.Id)` to request cancellation of the work itself.

## Periodic work

```csharp
using var schedule = scheduler.Schedule(
    $"gate-sync:{gateId}",
    () => new SynchronizeRasGateTask(gateId),
    TimeSpan.FromMinutes(1),
    new BackgroundTaskOptions
    {
        Queue = BackgroundTaskQueue.Synchronization,
        ConcurrencyKey = $"gate:{gateId}"
    },
    runImmediately: true);
```

When no deduplication key is supplied, the scheduler adds one based on the schedule ID. If a previous occurrence is
still active, the next occurrence shares it instead of creating overlapping work. Missed intervals are skipped; they are
not replayed in a burst after a pause.

## Recovery after restart

Persist the reason work is required, not the Engine's in-memory execution. For example, save a gate as `Initializing`,
then enqueue its initial sync. A recovery source scans such states when the host starts:

```csharp
public sealed class RasGateRecoverySource
    : IBackgroundTaskRecoverySource
{
    public async Task RecoverAsync(
        ISynchronizationEngine engine,
        CancellationToken cancellationToken)
    {
        // Read durable incomplete states and enqueue idempotent tasks.
    }
}
```

## Failure rules

All exceptions are retryable until `MaxAttempts` is reached. Throw
`NonRetryableBackgroundTaskException` for permanent validation or configuration failures. A missing handler is
automatically treated as non-retryable.

Timeout and cancellation are cooperative: handlers must observe the supplied
`CancellationToken`. The runtime cannot safely abort arbitrary managed code.

## Configuration

The `Synchronization` section controls:

- capacity and worker count for each queue;
- priority fairness interval;
- completed-task retention and cleanup interval;
- maximum number of tracked tasks.

Completed snapshots remain queryable until retention expires. The maximum tracked-task limit also bounds delayed and
scheduled executions in memory.

## Observability

The module logs task type and ID, never task payload. The meter name is
`RasHub.Synchronization` and exposes counters for enqueue, deduplication, rejection, start, retry, success, failure, and
cancellation, plus attempt duration. The `synchronization` readiness check becomes degraded at 80% capacity and
unhealthy when a queue or the task registry is full.
