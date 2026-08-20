# Tests

```text
tests/
├── UnitTests/
│   ├── RasHub.Contracts.UnitTests/
│   └── RasHub.Infrastructure.UnitTests/
└── IntegrationTests/
    ├── RasHub.BackgroundTasks.IntegrationTests/
    ├── RasHub.Infrastructure.IntegrationTests/
    └── RasHub.Web.IntegrationTests/
```

- Unit tests cover deterministic contracts, RAC parsing/adapters, and the
  composed RasGate session/resource gateways without external I/O.
- Infrastructure integration tests cover repositories, shadow-state stores,
  guarded publication, queries, and task handlers with isolated SQLite
  databases.
- Web tests exercise authentication, authorization, cached reads, explicit
  synchronization, and remote mutations through `WebApplicationFactory`.
- Background task tests cover queues, workers, scheduling, retry, cancellation,
  deduplication, concurrency, limits, shutdown, and diagnostics.

```bash
make test
make test-unit
make test-integration
```

Use PostgreSQL-backed tests for provider-specific SQL, migrations, indexes, or
locking behavior.
