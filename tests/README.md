# Tests

Test projects are organized first by test type and then by the production area
whose behavior they verify.

```text
tests/
├── UnitTests/
│   ├── RasHub.Contracts.UnitTests/
│   └── RasHub.Infrastructure.UnitTests/
└── IntegrationTests/
    ├── RasHub.Infrastructure.IntegrationTests/
    ├── RasHub.BackgroundTasks.IntegrationTests/
    └── RasHub.Web.IntegrationTests/
```

- Unit tests cover deterministic behavior that does not require I/O or an
  application host.
- Infrastructure integration tests execute EF Core against an in-memory SQLite
  relational database and cover mappings, repositories, queries, audit fields,
  and soft deletion.
- Web integration tests run the complete ASP.NET Core pipeline through
  `WebApplicationFactory`, replacing PostgreSQL with an isolated in-memory
  SQLite database.
- Background task integration tests run the in-memory queues, hosted workers,
  DI scopes, scheduling, retry, cancellation, recovery, and diagnostics together.

Add a test project for a production area when that area gains behavior worth
testing. Do not add projects merely to mirror empty production assemblies or
write tests for property-only DTOs.

SQLite keeps the default integration suite self-contained. PostgreSQL-specific
SQL, migrations, indexes, and provider behavior should be covered by a separate
container-backed suite when those concerns appear.
