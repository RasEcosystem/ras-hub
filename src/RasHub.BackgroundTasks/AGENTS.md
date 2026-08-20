# RasHub.BackgroundTasks Agent Guide

## Scope

This file applies to `src/RasHub.BackgroundTasks`. The repository-level `AGENTS.md` still applies. The behavioral test
suite for this library is `tests/IntegrationTests/RasHub.BackgroundTasks.IntegrationTests`; treat changes there as part
of the same subsystem.

Read this file before changing the engine. The implementation is intentionally compact, but several ordinary-looking
operations participate in admission, terminal publication, keyed ownership, or host lifecycle protocols. Preserve those
protocols unless the requested behavior explicitly changes them and the replacement is covered by deterministic
regression tests.

## Purpose and ownership boundary

`RasHub.BackgroundTasks` is a generic, in-process execution library. It owns:

- bounded admission and three isolated FIFO lanes;
- workers, per-attempt DI scopes, cooperative timeout and cancellation;
- retry delay calculation and delayed re-entry;
- active-execution deduplication and process-local concurrency keys;
- in-memory periodic scheduling;
- execution snapshots, bounded completed history, metrics, readiness, and host lifecycle supervision.

Feature assemblies own task records and `IBackgroundTaskHandler<TTask>` implementations. They also own the business
decision that work is idempotent enough to retry or reconstruct after restart. This library must remain independent of
RasHub feature, Web, EF Core, transport, and persistence projects. Do not move business orchestration, database access,
RasGate concepts, or feature-specific recovery into this project.

The engine is deliberately non-durable. Queues, delayed executions, deduplication, concurrency ownership, schedules, and
outcomes exist only in one process. A restart loses them. This library is execution machinery, not a job database or
distributed coordinator.

## Architecture map

| Area                      | Main types                                                                      | Responsibility                                                                                                                                   |
|---------------------------|---------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------|
| Composition               | `BackgroundTaskServiceCollectionExtensions`, `BackgroundTaskEngineOptions`      | Registers singleton infrastructure, the hosted service, startup validation, health check, and replaceable `TimeProvider`.                        |
| Admission and observation | `BackgroundTaskEngine`                                                          | Transactional enqueue, global active limit, active deduplication, cancel lookup, terminal accounting, lightweight completed history, statistics. |
| Execution state           | `BackgroundTaskExecution`                                                       | Thread-safe state machine and exactly-once terminal publication. Owns the execution cancellation source and caller completion task.              |
| Handler dispatch          | `BackgroundTaskDispatcher`, `BackgroundTaskInvoker<TTask>`                      | Creates a fresh async DI scope for every attempt and resolves the typed handler. Missing handlers are permanent failures.                        |
| Lane queues               | `InMemoryBackgroundTaskQueue`                                                   | Three lock-protected `LinkedList` lanes with O(1) exact removal, actual enqueue timestamps, and coalesced channel wakeups.                       |
| Delayed work              | `BackgroundTaskRescheduler`                                                     | Stable `(DueAt, Sequence)` priority queue for `NotBefore` and retries; transfers due accepted executions back to their lane.                     |
| Keyed serialization       | `BackgroundTaskConcurrencyGate`                                                 | Non-blocking, FIFO waiter registration per concurrency key, generation-safe leases, and granted-owner handoff.                                   |
| Attempts                  | `BackgroundTaskWorker`                                                          | Dequeue, key acquisition, start transition, timeout, dispatch, retry planning, failure classification, logging, and worker containment.          |
| Periodic schedules        | `PeriodicBackgroundTaskScheduler`                                               | Registration dictionary plus due-time heap, exact removal, generation-bound handles, factory dispatch, shutdown close/clear.                     |
| Host lifecycle            | `BackgroundTaskHostedService`, `BackgroundTaskRuntimeState`                     | Supervises rescheduler, scheduler, cleanup loop, and every worker; coordinates fail-fast host stop and graceful draining.                        |
| Diagnostics               | `BackgroundTaskMetrics`, `BackgroundTaskHealthCheck`, `BackgroundTaskTelemetry` | Low-cardinality .NET metrics and readiness based on lifecycle/liveness and capacity.                                                             |

All engine infrastructure is singleton and must remain thread-safe. Handlers keep their natural scoped lifetime; never
inject a scoped handler, `DbContext`, repository, or scoped service into these singletons.

## Public contracts

- `IBackgroundTask` marks an immutable work message. Keep messages small; pass identifiers and immutable values, not
  service providers, clients, tracked entities, scopes, or large object graphs.
- `IBackgroundTaskHandler<TTask>.ExecuteAsync` is resolved from a new DI scope on each attempt. It must propagate and
  observe the supplied token.
- `IBackgroundTask<TResult>` and `IBackgroundTaskHandler<TTask, TResult>` return small values only to a waiting
  in-process caller. Typed values are not durable, retained in completed snapshots, or public wire contracts.
- `IBackgroundTaskEngine.Enqueue` either returns a live handle or throws. A rejected or faulted admission must leave no
  active reservation, dedup entry, queue/rescheduler placement, or public execution.
- `BackgroundTaskHandle.WaitAsync` only cancels the caller's wait. It does not cancel shared work. `Cancel(id)` requests
  execution cancellation.
- `BackgroundTaskResult` is the caller-owned terminal result. Failed results carry the actual exception graph; do not
  serialize it or expose it at an HTTP boundary without sanitization.
- `BackgroundTaskSnapshot` is the retained observational form. Completed history stores snapshots, not execution
  objects, task payloads, completion sources, or exception graphs.
- `IBackgroundTaskScheduler.Schedule` owns one in-memory registration. Disposing its handle removes only the exact
  registration generation that created that handle.
- `BackgroundTaskOptions` selects the lane, first-run time, attempt timeout, retry policy, deduplication key, and
  concurrency key. Timeout applies to one attempt, not queue/key wait, retries, or the caller's total wait.
- `NonRetryableBackgroundTaskException` bypasses retries. Other handler failures, including attempt timeouts, are
  retryable while attempts remain.
- `BackgroundTaskRejectedException` reports stopped admission, global active saturation, or external lane capacity
  exhaustion.
- `BackgroundTaskTelemetry.MeterName` (`RasHub.BackgroundTasks`) is a public instrumentation name. The readiness check
  is registered as `background-tasks` with the `ready` tag.

Current configurable defaults are: lane capacities `256 / 1024 / 256`, workers
`8 / 16 / 2`, `MaxActiveTasks` is `10_000`, and completed history is capped at `1_000` snapshots with 10-minute
retention and one-minute cleanup. Per execution, the defaults are three attempts, one-second retry delay, factor `2`,
one-minute maximum retry delay, and five-minute attempt timeout. Worker counts validate to `1..1024` per lane and
`<= 2048` total; keys are limited to 512 characters and schedule IDs to 200. Treat these as tunable defaults, not
execution-protocol guarantees.

The three lanes are semantic API, not numerical priority levels:

- `Interactive`: caller-facing work that should get prompt capacity;
- `Synchronization`: regular remote/background synchronization;
- `Maintenance`: housekeeping.

Each lane has independent workers and external queue capacity. Deduplication and concurrency keys are global across
lanes within this process. Deduplication identity is runtime task type plus deduplication key, so duplicates share the
first active execution and its original options. Concurrency keys are not type-scoped: different task types using the
same string serialize behind the same process-local owner.

## Main execution flows

### Enqueue and admission

1. Validate options before reserving capacity.
2. Under `_admissionSync`, reject stopped admission, reuse a live deduplicated execution, or reserve one global
   `MaxActiveTasks` slot.
3. Admission is a transaction: create the execution, claim deduplication, place it in a lane or the rescheduler, and
   publish it in `_activeExecutions` as the commit point.
4. Any exception before commit performs exact rollback of registry, queue/rescheduler, deduplication, and the active
   reservation. A worker that already dequeued a rolled-back object must fail the exact-active-membership check and
   discard it.
5. Only after commit publish enqueue metrics and return the handle.

Do not expose the execution in `_activeExecutions` before queue/delayed admission. Do not move failure-prone clock,
queue, or registry operations outside the admission rollback boundary.

### Worker attempt and retry

1. A lane worker dequeues an execution. Terminal entries are skipped.
2. Unkeyed work proceeds immediately. Keyed work either receives a lease or is retained by the gate as a waiter; the
   worker never blocks waiting for a key and remains available for unrelated work.
3. `TryStartExecution` verifies exact active membership and crosses `Pending -> Running`, increments the attempt, and
   records the first start time.
4. The dispatcher creates a new async scope and invokes the typed handler. The attempt token combines execution
   cancellation with the optional timeout. The hosted-process token stops infrastructure loops; it is intentionally not
   used as a direct handler-attempt token.
5. A retry transition returns the execution to `Pending`. Calculate and capture the retry plan while handling the
   failure, release the concurrency lease, and only then publish the retry to the rescheduler. Publishing a zero-delay
   keyed retry while the old lease is still owned can orphan the execution.
6. Retry delay is exponential, capped by `MaxRetryDelay`; due-time arithmetic is clamped to the supported UTC range.
   Accepted delayed/retry work must re-enter its lane even when new external admission has filled that lane.

### Cancellation and terminal publication

- `Cancel(id) == true` means the first cancellation request was accepted and asynchronous signal processing was
  started/tracked. It does not mean every user callback has finished. Missing, repeated, or terminal cancellation
  returns `false`.
- Pending cancellation changes state immediately but keeps the active reservation until cancellation callbacks finish
  and terminal publication runs. This bounds retained callback graphs by active admission.
- Cancellation requested during a running attempt wins over a later success or non-cancellation exception: the final
  outcome is `Canceled`, not `Succeeded` or `Failed`.
- User cancellation callbacks may throw or block. They never run while engine/execution locks are held; their tasks are
  observed and tracked so shutdown can drain them.
- A terminal transition invokes the engine finalizer exactly once. The finalizer removes queue/rescheduler/gate and
  dedup state, stores a lightweight completed snapshot, removes the active execution, releases capacity, records
  history/timing/metrics, and only then makes handle completion observable.
- Never publish caller completion before terminal accounting. Tests rely on observing `ActiveTasks == 0` immediately
  after awaiting a terminal result.

### Periodic scheduling

- The scheduler defaults the deduplication key to `schedule:{scheduleId}` when the caller supplies none.
- Due registrations schedule their next run from current time before invoking the factory; missed intervals are not
  replayed.
- Factories run synchronously under the registration's dispatch boundary. They must only construct an immutable task
  message. Do not perform I/O or capture scoped services.
- Successful `Remove` waits for an already-started dispatch and guarantees no later dispatch starts for that
  registration. Physical heap removal is required to release factory closures.
- Schedule handles are generation-bound. An old handle must not remove a replacement schedule with the same ID.
- Scheduler shutdown atomically closes admission, marks registrations removed, clears the heap/dictionary, releases
  closures, and rejects later schedules.

### Host startup, failure, and shutdown

The hosted service supervises three infrastructure loops (`rescheduler`, `scheduler`, `registry-cleanup`) plus every
configured lane worker. `BackgroundTaskRuntimeState` tracks `NotStarted`, `Starting`, `Running`, `Stopping`, `Stopped`,
and `Faulted`, along with expected/live process counts.

- Every process yields once before consuming work so a preloaded synchronous lane cannot prevent the supervisor from
  registering the remaining loops.
- Unexpected completion or fault of any child marks the runtime faulted, signals application stop, closes scheduler and
  engine admission, cancels siblings, joins all sibling processes/attempt scopes, preserves the original child
  exception, and leaves readiness unhealthy.
- Normal shutdown closes schedule admission first, then engine admission. Cancel-all first snapshots/prepares every
  active execution and only then starts signals; starting signals during weak dictionary enumeration can skip work.
- Shutdown joins infrastructure processes and drains tracked cancellation signals before `MarkStopped`. Do not replace
  this with detached fault observation or return while attempt scopes/callbacks can still use disposed DI services.
- Cancellation and timeout remain cooperative. A handler, callback, or schedule factory that never returns can delay
  shutdown until the outer host shutdown deadline; the library does not forcibly terminate user code.

## Concurrency and lock invariants

- `_admissionSync` serializes enqueue commit, exact start membership, cancel ownership, terminal admission cleanup, and
  stop admission. Keep work under it bounded; never run handlers, factories, cancellation callbacks, or blocking I/O
  there. Existing metrics on this path must remain no-throw.
- `BackgroundTaskExecution` releases its state lock before invoking terminal finalization. Never call the engine
  finalizer while holding the execution lock.
- Queue, rescheduler, and concurrency-gate locks protect compound state only. Gate handoff dispatches to the queue after
  releasing the gate lock; rescheduler dispatches after releasing its heap lock.
- Scheduler nested lock order is `ScheduleRegistration.DispatchSync` then scheduler `_sync`; do not introduce the
  reverse order. Do not hold scheduler `_sync` while running a factory.
- A keyed owner has a generation and phase (`Running` or `Granted`). Lease disposal and terminal removal must validate
  the exact owner, state, and generation. Stale leases are no-ops.
- FIFO for a concurrency key means waiter registration order at the gate. With multiple workers, do not claim a strict
  global order based on original enqueue timestamps.
- Public lane admission is bounded. `EnqueueAccepted` intentionally bypasses lane capacity for already-counted
  retry/delayed/granted work so it cannot be stranded. A lane can therefore temporarily exceed its configured queue
  capacity, while the global `MaxActiveTasks` limit remains the hard active-execution admission bound.
- Queue removal must remain O (1). Canceled queued work is physically removed and frees lane capacity immediately.
- Rescheduler ordering is stable by `(DueAt, Sequence)` and rechecks actual dequeued priority and current UTC time.
  Custom/test clocks may move backward; do not assume `GetUtcNow()` is monotonic.
- Use the registered `TimeProvider` for all engine time and timers. Keep attempt timeouts and registry-cleanup
  `PeriodicTimer` values within `BackgroundTaskTimerLimits`; unsupported values must fail validation before
  admission/startup. Scheduler/retry waits are sliced into at most one-day timers and may span longer intervals, but
  their due times must remain inside the supported `DateTimeOffset` range.
- Metrics are correctness-isolated: synchronous `MeterListener` exceptions must not reject work or kill workers. Active
  `+1` is emitted before the enqueued counter because an enqueued listener may re-enter and cancel the task. Keep metric
  tags low-cardinality (`task.type`, `queue`), never task IDs, keys, payloads, or exception messages.

## Diagnostics and retained state

Completed history is a bounded dictionary of `BackgroundTaskSnapshot` plus a physical linked-list index. The maximum
count is enforced at terminal publication; `CompletedTaskRetention` is enforced by the periodic cleanup process. Cleanup
must physically unlink arbitrary expired IDs; prefix-only tombstone cleanup leaks memory when UTC moves backward.

Do not put `BackgroundTaskExecution`, `BackgroundTaskResult`, task payloads, or exceptions back into completed history.
Callers may retain exception graphs through their handles, but the engine must release them independently while keeping
the snapshot queryable. Worker locals that cross `await` are explicitly cleared so an idle worker does not retain its
last execution indefinitely.

Readiness behavior is part of the operational contract:

- `NotStarted`/`Starting`: degraded;
- `Running` with all expected processes: capacity decides healthy/degraded/unhealthy;
- `Faulted`, `Stopping`, `Stopped`, or a live/expected mismatch while running: unhealthy;
- capacity at 80%: degraded; exhausted global or lane capacity: unhealthy.

The meter publishes lifecycle counters, active balance, attempt duration, lane length/oldest age, delayed/overdue
counts, concurrency owners/waiters, and live/expected process gauges. The library publishes the `Meter`; Web owns the
optional OpenTelemetry exporter.

## Tests

There is no separate unit-test project for this library. The in-process Host-based suite is
`tests/IntegrationTests/RasHub.BackgroundTasks.IntegrationTests`. It has no external database or network dependency and
uses controllable handlers, synchronization primitives, `TimeProvider`, metrics listeners, weak references, and focused
reflection where an internal memory invariant must be observed.

Test-file responsibilities:

- `BackgroundTaskEngineTests.cs`: composed enqueue/execute smoke tests and typed-result dispatch.
- `BackgroundTaskEngineBehaviorTests.cs`: public failure, retry, timeout, cancel, dedup, keyed serialization, lane FIFO,
  periodic schedule, rejection, pre-start cancellation, and missing-handler behavior.
- `.Admission.cs`: transactional rollback when clock/queue admission throws, including dedup cleanup and recovery.
- `.Hosting.cs`: startup/stop races, liveness/readiness, process fault containment, sibling/scope join,
  cancellation-signal drain, and option-bound validation.
- `.Metrics.cs`: exact active balance, observable gauges, throwing listeners, and listener re-entrancy.
- `.Operations.cs`: lane isolation, full-queue shutdown cancellation, capacity health, timing retention, and bounded
  completed history.
- `.QueuesAndScheduling.cs`: O (1) cancellation cleanup, multi-worker wake/exactly-once behavior, accepted re-entry into
  a full lane, stable equal-due order, keyed FIFO/granted cancellation/stale leases, immediate keyed retry,
  closure/payload collection, remove-vs-dispatch, and scheduler shutdown.
- `.Retention.cs`: payload/exception collection while snapshots remain and non-monotonic physical history cleanup.
- `.Scheduler.cs`: old-handle/replacement generation safety.
- `BackgroundTaskEngineCoreRegressionTests.cs`: terminal accounting order, cancel/failure races, throwing/blocking
  callbacks, fan-out, callback-held capacity, safe exception messages, extreme timer/retry/retention values, and
  rejected admission visibility.
- `BackgroundTaskEngineTestDoubles.cs`: shared deterministic handlers and probes; reuse these before adding sleeps.

When changing concurrency, add a deterministic interleaving regression. Prefer `TaskCompletionSource`,
`ManualResetEventSlim`, a controllable handler, a custom `TimeProvider`, or `MeterListener` barrier. Do not use timing
luck or long sleeps to demonstrate a race. Memory-retention fixes need weak-reference/GC coverage, not only collection
counts.

## Known limits and deliberate compromises

- No persistence, restart recovery, leases across processes, distributed locks, or cross-instance deduplication.
- Retry safety is a caller responsibility; the engine cannot infer idempotency.
- Cooperative cancellation cannot stop a handler that ignores its token.
- Schedule factories have no cancellation token and execute synchronously. A blocking factory also blocks successful
  removal at its documented synchronization boundary.
- Per-key fairness is process-local waiter registration order, not a durable or globally timestamped order.
- Internal accepted re-entry may temporarily exceed a lane's configured capacity. Changing it to ordinary bounded
  enqueue reintroduces retry/`NotBefore`/handoff starvation.
- `BackgroundTaskResult.Exception` intentionally preserves the handler exception for in-process callers. Snapshot
  history keeps only a bounded safe message; external adapters remain responsible for sanitization.
- Metrics listeners execute synchronously in .NET. The engine contains listener exceptions and ordering re-entrancy, but
  a malicious listener that blocks forever can still block its producer thread.

## Verification commands

Run from the repository root. Iterate with a focused filter, then run the whole subsystem and finally the solution when
production code changes.

```bash
dotnet restore RasHub.sln

dotnet format RasHub.sln --no-restore --verify-no-changes

dotnet build RasHub.sln \
  --configuration Release \
  --no-restore \
  --warnaserror \
  -m:1

dotnet test \
  tests/IntegrationTests/RasHub.BackgroundTasks.IntegrationTests/RasHub.BackgroundTasks.IntegrationTests.csproj \
  --configuration Release \
  --no-build \
  --no-restore \
  -m:1

dotnet test RasHub.sln \
  --configuration Release \
  --no-build \
  --no-restore \
  -m:1
```

For a concurrency-sensitive change, repeat the complete library suite rather than only the new test:

```bash
for iteration in {1..20}; do
  dotnet test \
    tests/IntegrationTests/RasHub.BackgroundTasks.IntegrationTests/RasHub.BackgroundTasks.IntegrationTests.csproj \
    --configuration Release \
    --no-build \
    --no-restore \
    -m:1 \
    --logger 'console;verbosity=quiet' || exit 1
done
```

## Highest-risk change areas

- `BackgroundTaskEngine`: admission transaction, exact membership, cancel ownership, terminal finalization, active
  count, dedup, history, and shutdown fan-out are one correctness boundary.
- `BackgroundTaskExecution`: state transitions and exactly-once terminal publication. Never invoke external engine work
  under its state lock.
- `BackgroundTaskWorker`: attempt-token composition, key lease lifetime, retry publication order, and exception
  containment. A fault here must not silently kill a worker or leave `Running` work.
- `InMemoryBackgroundTaskQueue` / `BackgroundTaskRescheduler`: wakeup signals, physical removal, stable ordering, UTC
  rollback, and accepted re-entry. Lost signals strand work; stale waiters can consume future signals.
- `BackgroundTaskConcurrencyGate`: owner phases, FIFO waiter list, terminal removal, generation-safe stale leases, and
  dispatch outside the gate lock.
- `PeriodicBackgroundTaskScheduler`: registration identity, heap/dictionary consistency, dispatch lock order, factory
  closure lifetime, remove boundary, and shutdown admission.
- `BackgroundTaskHostedService`: process startup visibility, fail-fast signaling, sibling join, attempt-scope lifetime,
  cancellation drain, and readiness state. Never detach child faults or mark stopped before all owned work is joined.
- `BackgroundTaskMetrics`: instrumentation is synchronous user-extensible code. Preserve no-throw behavior, active
  balance, low cardinality, and re-entrant ordering.
- DI/options registration: timer and worker bounds are startup safety checks, not cosmetic validation.

Before editing any of these, read its focused regression file. After editing, inspect the full diff for changed lock
order, publication order, ownership transfer, and cleanup paths—not only the nominal success path.
