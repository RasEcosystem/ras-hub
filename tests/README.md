# Tests

Test projects are organized first by test type and then by the production area
whose behavior they verify.

```text
tests/
└── IntegrationTests/
    └── RasHub.Synchronization.IntegrationTests/
```

- Synchronization integration tests run the in-memory queues, hosted workers,
  DI scopes, scheduling, retry, cancellation, and recovery together.

Add a test project for a production area when that area gains behavior worth
testing. Do not add projects merely to mirror empty production assemblies or
write tests for property-only DTOs.
