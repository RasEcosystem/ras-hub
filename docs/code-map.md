# RasHub code map

This file is a navigation aid: start here to trace a change across the current
codebase. Binding development rules live in [`AGENTS.md`](../AGENTS.md); RAC
compatibility and queue mechanics have their own detailed documents. All code
paths below are relative to the repository root.

## Projects

| Project or area | Owns | Start reading |
|---|---|---|
| `src/RasHub.Domain` | Hub-owned persisted entities and entity abstractions | `RasGate`, `RasCluster`, `RasInfobase` |
| `src/RasHub.Application/RasGates` | Resource ports, normalized remote models, task messages and handlers, safe failure types | `Abstractions`, `Models`, `Tasks` |
| `src/RasHub.Infrastructure/Database` | `RasHubDbContext`, repositories, read projections, snapshot stores, guarded publisher, EF interceptors and migrations | `RasGateSyncPublisher`, `Queries`, `EntityTypeConfigurations` |
| `src/RasHub.Infrastructure/RasGates` | Endpoint validation, transport/session, resource gateways, RAC adapters and parsers | [RAC compatibility](rac-compatibility.md), then `Client/RasGateSession.cs`, the matching gateway and `Rac/<Resource>` |
| `src/RasHub.BackgroundTasks` | Generic in-process queues, workers, retries, scheduling and diagnostics | its [`README`](../src/RasHub.BackgroundTasks/README.md) and [`AGENTS.md`](../src/RasHub.BackgroundTasks/AGENTS.md) |
| `src/RasHub.Web` | HTTP, Blazor, Identity, authorization, monitoring and composition | `Program.cs`, `Controllers`, `Api`, `Infrastructure` |
| `src/RasHub.Contracts` | Versioned public wire contracts shared with clients | separate Git submodule |

`RasHubDbContext` stores Hub and shadow state. Web's `ApplicationDbContext`
stores ASP.NET Identity state. They have separate migrations and no shared
atomic transaction.

## Feature entry points

| Behavior | Entry point |
|---|---|
| RasGate CRUD API | `src/RasHub.Web/Controllers/RasGates/RasGatesController.cs` |
| Blazor RasGate administration | `src/RasHub.Web/Infrastructure/RasGates/RasGateAdministrationService.cs` |
| Shared RasGate configuration lifecycle | `src/RasHub.Application/RasGates/Services/RasGateRegistry.cs` |
| Cached and synchronized status | `src/RasHub.Web/Controllers/RasGates/RasGateStatusController.cs` |
| Cached cluster reads | `src/RasHub.Web/Controllers/RasClusters/RasGateClustersController.cs` |
| Cluster synchronization | `src/RasHub.Web/Controllers/RasClusters/RasGateClusterSynchronizationController.cs` |
| Cluster create/update/remove | `src/RasHub.Web/Controllers/RasClusters/RasGateClusterAdministrationController.cs` |
| Cached infobase reads | `src/RasHub.Web/Controllers/RasInfobases/RasClusterInfobasesController.cs` |
| Infobase synchronization | `src/RasHub.Web/Controllers/RasInfobases/RasClusterInfobaseSynchronizationController.cs` |
| Hosted Gate status monitoring | `src/RasHub.Web/Infrastructure/RasGates/RasGateMonitoringService.cs` |
| DI and handler registration | `src/RasHub.Web/Program.cs` and `src/RasHub.Infrastructure/Extensions/ServiceCollectionExtensions.cs` |

The API controller and Blazor administration service delegate RasGate writes
to `RasGateRegistry`. Keep shared normalization, lifecycle, and shadow
invalidation there; authorization and presentation remain adapter-specific.

## Real execution flows

### Cached API query

```text
Controller -> ActiveRasGateLookup -> Ras*Queries
           -> AsNoTracking EF projection -> Contracts response/model
           -> ApiResponse<T>
```

Gate-scoped status, cluster, and infobase queries use `ActiveRasGateLookup`;
top-level Gate queries go directly to `RasGateQueries`. Cached queries call
Infrastructure projections directly, do not use Application handlers, and do
not contact RasGate.

### Explicit synchronization or remote mutation

```text
Controller -> ActiveRasGateLookup + optional parent/resource lookup
 -> InteractiveTaskRunner -> IBackgroundTaskEngine
 -> lane worker and per-Gate concurrency key -> fresh attempt DI scope
 -> Application task handler -> tracked RasGate + captured ConfigurationRevision
 -> status/resource gateway -> internal RasGate session -> RasGate
                                                   -> RAC (status/resources)
 -> IRasGateSyncPublisher -> [snapshot store for cluster/infobase state]
 -> one SaveChangesAsync -> controller returns the endpoint-specific response
```

Capability preflight and the following resource call create separate session
instances but share the RAC-version cache keyed by Gate ID and configuration
revision. Create and update publish an authoritative `info` read-back. Remove
publishes a guarded local deletion only after confirmed remote success.

### Hosted monitoring

```text
RasGateMonitoringService -> PeriodicTimer -> scoped RasGateQueries
 -> CheckRasGateStatusTask in Synchronization lane -> normal handler flow
```

Monitoring refreshes the aggregate RasGate/RAC status only. It does not use the
generic periodic scheduler, and it does not schedule cluster or infobase
synchronization. A reachable Gate with `available: false` RAC is recorded as
degraded; a failed RAC probe records RAC as unknown without discarding the
successful Gate observation.

### Shadow publication and Gate configuration

Handlers capture `ConfigurationRevision` before remote I/O. The publisher
obtains the tracked Gate, guards revision plus active/deleted state, applies a
complete collection or targeted change, updates observation metadata, and saves
once. Complete collections can remove absent children; targeted upserts leave
siblings unchanged.

Status publication writes the RasGate and RAC observations in the same guarded
save. Remote-identity, deactivation, deletion, and restoration changes clear
derived state and advance the configuration revision, preventing an in-flight
result from restoring stale state.

Gate-write mechanics live under `Database/Interceptors` and
`EntityTypeConfigurations`. Remote keys are parent-scoped—clusters by Gate and
infobases by cluster—and are distinct from Hub IDs.

## Change routing

| Change | Follow through |
|---|---|
| Public request/response | Contracts submodule -> Web controller -> Infrastructure query projection when cached -> OpenAPI and Contracts/Web tests |
| Remote-derived persisted field | Application snapshot -> RAC deserializer -> Domain entity -> EF mapping/migration -> snapshot store -> query contract -> tests |
| New read/sync operation | Application gateway/task -> Infrastructure gateway/adapter/DI -> publisher/store when shadow state changes -> Web controller/task options/handler registration -> tests |
| Cluster mutation | Contract mapping -> task/options -> gateway/adapter -> read-back or guarded removal -> unknown-outcome and Web tests |
| RAC output-format-only change | Resource deserializer and resolver registration/tests; keep the command adapter |
| RAC command or outcome change | Operation adapter -> typed interface plus descriptor registration -> capability and gateway tests |
| Persistence invariant | Entity/configuration/interceptor/store/publisher/migration plus Infrastructure integration tests |
| Retry/dedup/concurrency policy | `src/RasHub.Web/Infrastructure/RasGates/RasGateTaskOptions.cs` plus handler/API tests |
| Generic engine mechanics | `RasHub.BackgroundTasks` plus its integration suite and nested agent guide |
| Authentication or authorization | Web authentication/authorization, pipeline registration and Web integration tests |

## Detailed references

- [RAC compatibility boundary](rac-compatibility.md)
- [Background task engine](../src/RasHub.BackgroundTasks/README.md)
- [Test suites](../tests/README.md)
- [Deployment and health](../deploy/README.md)
