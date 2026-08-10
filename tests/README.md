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

- Unit tests cover deterministic logic without I/O.
- Infrastructure tests use an isolated in-memory SQLite database.
- Web tests exercise the ASP.NET Core pipeline through `WebApplicationFactory`.
- Background task tests cover queues, workers, scheduling, retry, cancellation,
  deduplication, concurrency, limits, shutdown, and diagnostics.

```bash
make test
make test-unit
make test-integration
```

Use PostgreSQL-backed tests for provider-specific SQL, migrations, indexes, or
locking behavior.
