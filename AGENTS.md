# RasHub Agent Guide

## Scope

- These rules apply to the whole repository unless a deeper `AGENTS.md` says otherwise.
- Preserve unrelated working-tree changes and keep each change scoped to the requested behavior.
- `src/RasHub.Contracts` is a Git submodule. Change it only when the task intentionally changes the shared wire contract, and keep the submodule pointer update explicit.

## Architecture and Dependencies

- Keep `RasHub.Domain` free of Web, Infrastructure, transport, EF Core, and public-contract dependencies.
- Keep `RasHub.BackgroundTasks` generic. It owns execution mechanics and must not depend on RasHub feature projects.
- `RasHub.Application` may depend on Domain and BackgroundTasks. It owns RasGate ports, normalized application models, and feature task handlers.
- `RasHub.Infrastructure` may depend on Application and Contracts; it implements Application ports and owns EF Core, PostgreSQL, contract query projections, RasGate HTTP transport, and RAC parsing.
- `RasHub.Web` may depend on Infrastructure, BackgroundTasks, and Contracts; it is the composition root and owns HTTP, Blazor, Identity, configuration, hosted monitoring, and process startup.
- `RasHub.Contracts` contains versioned wire requests, responses, models, pagination, and the API envelope; keep it independent of server implementation projects.
- Do not add reverse references from lower layers to Web or Infrastructure.
- Put reusable feature orchestration in Application when it must be shared by more than one adapter; keep HTTP-only behavior in Web.
- Do not add layers, repositories, mediators, or interfaces merely to mirror existing types. Every abstraction must isolate a concrete dependency or shared policy.

## Domain and Application

- Domain entities represent Hub-owned persisted state. Do not put HTTP routes, JSON envelopes, RAC stdout, or UI concerns in them.
- Pass identifiers and small immutable messages to background tasks; do not pass `DbContext`, tracked entities, clients, or service providers.
- Application handlers use the necessary Application ports rather than concrete transport or EF implementations.
- Keep concrete RasGate transport DTOs and parsing implementations in Infrastructure; expose normalized status and snapshot models to Application.
- Preserve asynchronous APIs and propagate the supplied `CancellationToken` through every I/O call.

## EF Core and Persistence

- `RasHubDbContext` owns Hub state; `ApplicationDbContext` owns ASP.NET Identity state. Do not assume cross-context atomic transactions.
- Use one scoped `RasHubDbContext` for repositories, snapshot stores, and `IUnitOfWork` within one request or task attempt.
- Use `AsNoTracking` for read-only projections. Track entities only when the same scope will mutate them.
- Keep read-only contract projections in query services; use the repository and unit of work for tracked mutations.
- Audit fields and soft deletion are applied by `AuditSoftDeleteInterceptor`; use normal EF state changes instead of duplicating this logic.
- Respect global soft-delete filters. Use `IgnoreQueryFilters` only for an explicit restore, reconciliation, retention, or administrative operation.
- Configure mappings, indexes, constraints, relationships, and filters in `EntityTypeConfigurations`, not ad hoc in callers.
- Add schema changes through migrations in the owning context and migrations assembly; keep Identity migration history separate from Hub migration history.
- Keep a shadow-snapshot mutation in one `SaveChangesAsync` so reconciliation and its observation metadata commit atomically.
- `IRasClusterSnapshotStore` treats its input as a complete authoritative snapshot. Never pass a partial or unvalidated remote result to it.
- Preserve the unique identity of a remote cluster as `(RasGateId, ExternalId)` and the existing restore-on-reappearance behavior.
- Include cancellation tokens in EF queries and saves.
- When a change depends on PostgreSQL-specific SQL, migrations, indexes, JSON, locking, or provider behavior, cover that behavior with PostgreSQL-backed tests rather than SQLite alone.

## RasGate and RAC Boundary

- Reach RasGate through `IRasGateClient`/`IRasGateClientFactory`; do not issue RasGate HTTP calls directly from Web, Domain, or task handlers.
- Keep RasGate HTTP DTOs, endpoint construction, headers, and RAC key-value parsing private to Infrastructure.
- Reuse the registered transport/`HttpClient`; do not create an `HttpClient` per operation.
- Validate the remote envelope, timeout flag, exit code, and parsed output before changing Hub state.
- Treat RAC output as untrusted input. Parsing failures must fail the operation, not silently publish guessed data.
- Handle RAC/RasGate version and format differences inside the boundary adapter, not in Domain entities or public Hub contracts.
- Treat both RasHub user API keys and RasGate API keys as secrets. Never log them, include them in task payloads, expose them in errors, or return stored Gate keys from API models.
- Convert transport, protocol, and parsing failures to sanitized errors at the Web boundary; never expose raw remote bodies, stdout, or exception details.
- Add representative parser/client inputs whenever supported remote fields, enums, envelopes, commands, or versions change.

## Background Tasks

- Feature projects own task records and handlers; BackgroundTasks owns queues, workers, retries, scheduling, state transitions, and diagnostics.
- Register task handlers with their natural scoped lifetime. The dispatcher creates a new DI scope for every attempt.
- Never inject a scoped handler, repository, or `DbContext` into the singleton engine, worker, scheduler, or hosted service; create a scope when background infrastructure needs scoped services.
- Use `Interactive` only when a caller waits for prompt completion, `Synchronization` for routine remote synchronization, and `Maintenance` for housekeeping.
- Set a stable deduplication key for the same logical work. Deduplication spans lanes for the same runtime task type and key; duplicate callers share the first active execution and its options.
- Give tasks that may conflict while reading or publishing the same Gate state the same per-Gate concurrency key, following the existing `ras-gate:{id}` convention.
- Concurrency and deduplication keys are process-local. Do not treat them as database concurrency control or cross-instance coordination.
- Retries are valid only for idempotent work. Do not enable automatic retries for a mutating operation unless its idempotency is established.
- Use `NonRetryableBackgroundTaskException` for permanent validation or configuration failures; do not retry merely because the engine can.
- Cancellation and timeouts are cooperative. Handlers must observe the supplied token and must not swallow cancellation.
- `BackgroundTaskOptions.Timeout` limits one attempt, not queue wait, concurrency wait, retries, or the caller's total wait; set a separate caller deadline when one is required.
- Canceling `BackgroundTaskHandle.WaitAsync` cancels only the caller's wait; cancel the engine execution explicitly only when shared work should stop.
- The queues, deduplication registry, delayed work, outcomes, and schedules are in-memory. Never rely on them as durable business state.
- Critical work must be reconstructible from persisted business state and restored idempotently through a recovery source.
- Fetch and validate remote data before atomically publishing a complete local result. A failed attempt must leave the previous valid shadow state intact.
- Keep task logs structured and limited to safe identifiers, task type, attempt, duration, and outcome; do not log task payloads.

## Dependency Injection and Time

- Singleton services must be stateless or explicitly thread-safe and may depend only on singleton-safe collaborators.
- HTTP requests and background attempts may use scoped EF services; hosted services must resolve them through a fresh scope.
- Use the registered `TimeProvider` for background task timing, delays, and time-dependent logic so behavior remains testable.
- Keep library registrations in the owning project's service-collection extension; keep Web-specific registrations, application composition, and options binding in `RasHub.Web/Program.cs`.
- Bind process options in Web and use startup validation for constraints that can be checked without external I/O.

## HTTP API and Contracts

- Keep external endpoints under the existing `/api/v1` surface and return the shared `ApiResponse<T>` envelope consistently.
- Use Contracts request/response types at the HTTP boundary; never serialize EF entities, Identity entities, transport DTOs, or exceptions directly.
- Validate request shape at the boundary and pass business-safe values inward. Keep error codes stable and messages free of implementation details.
- Preserve HTTP status, response-envelope, OpenAPI, and trace-ID behavior when adding controller results or middleware.
- Every endpoint must declare its intended authentication and authorization requirements; keep API-key and cookie authentication schemes distinct.
- Propagate request cancellation into EF queries, engine waits, and direct I/O.
- Treat published Contracts changes as compatibility-sensitive. Prefer additive evolution and update Contracts, OpenAPI, and Web integration tests together.
- Use `Request`, `Response`, and `Model` suffixes consistently for public wire types; use Infrastructure-private names for transport DTOs.

## Testing and Verification

- Use xUnit v3. Name test methods `Operation_condition_expected_result` and keep each test focused on one observable behavior.
- Put deterministic, no-I/O tests in UnitTests; use the existing Infrastructure, BackgroundTasks, and Web integration suites for composed behavior.
- Web integration tests should exercise the ASP.NET Core pipeline through `WebApplicationFactory`; do not replace the behavior under test with the same implementation logic.
- BackgroundTasks tests must cover state transitions, retry, cancellation, timeout, deduplication, concurrency keys, queue rejection, and shutdown when those mechanics change.
- Persistence changes must cover create/update, query filters, soft deletion, restore/reconciliation, constraints, and transaction failure where relevant.
- RasGate changes require success, malformed response, remote error, timeout/cancellation, and representative RAC input coverage.
- Security tests must verify server-side enforcement through the relevant unauthenticated, authenticated, blocked, or revoked path, not only UI rendering.
- Prefer `TimeProvider`, controllable handlers, and synchronization primitives over timing-sensitive sleeps.
- Do not add a test project solely to mirror a production assembly with no behavior worth testing.
- Run the narrowest affected test project while iterating, then run `dotnet test RasHub.sln` or `make test` before handoff when the environment permits.
