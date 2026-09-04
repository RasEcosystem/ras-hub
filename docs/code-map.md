# RasHub code map

This file is a navigation aid: start here to trace a change across the current
codebase. RAC compatibility and queue mechanics have their own detailed
documents. All code paths below are relative to the repository root.

## Projects

| Project or area | Owns | Start reading |
|---|---|---|
| `src/RasHub.Domain` | Hub-owned persisted entities and entity abstractions | `RasGate`, `RasEndpoint`, `RasCluster`, `RasInfobase` |
| `src/RasHub.Application/RasEndpoints` | Endpoint lifecycle, Gate assignment, address validation, target resolution, and execution guards | `Services`, `Models`, `Exceptions` |
| `src/RasHub.Application/RasGates` | Resource ports, normalized remote models, task messages and handlers, safe failure types | `Abstractions`, `Models`, `Tasks` |
| `src/RasHub.Infrastructure/Database` | `RasHubDbContext`, repositories, read projections, snapshot stores, guarded publishers, EF interceptors and migrations | `RasEndpointSyncPublisher`, `RasGateSyncPublisher`, `Queries`, `EntityTypeConfigurations` |
| `src/RasHub.Infrastructure/RasGates` | Endpoint validation, transport/session, resource gateways, RAC adapters and parsers | [RAC compatibility](rac-compatibility.md), then `Client/RasGateSession.cs`, the matching gateway and `Rac/<Resource>` |
| `src/RasHub.BackgroundTasks` | Generic in-process queues, workers, retries, scheduling and diagnostics | its [`README`](../src/RasHub.BackgroundTasks/README.md) and integration tests |
| `src/RasHub.Web` | HTTP, Blazor, Identity, authorization, monitoring and composition | `Program.cs`, `Controllers`, `Api`, `Infrastructure` |
| `src/RasHub.Contracts` | Versioned public wire contracts shared with clients | separate Git submodule |

`RasHubDbContext` stores Hub and shadow state. Web's `ApplicationDbContext`
stores ASP.NET Identity state. They have separate migrations and no shared
atomic transaction.

## Feature entry points

| Behavior | Entry point |
|---|---|
| RasGate registration reads | `src/RasHub.Web/Controllers/RasGates/RasGateQueryController.cs` |
| RasGate registration search | `src/RasHub.Web/Controllers/RasGates/RasGateSearchController.cs` |
| RasGate registration administration | `src/RasHub.Web/Controllers/RasGates/RasGateAdministrationController.cs` |
| Blazor RasGate administration | `src/RasHub.Web/Infrastructure/RasGates/RasGateAdministrationService.cs` |
| Shared RasGate configuration lifecycle | `src/RasHub.Application/RasGates/Services/RasGateRegistry.cs` |
| RasGate status shadow and live observation | `src/RasHub.Web/Controllers/RasGates/RasGateStatusController.cs` |
| RAS endpoint registration and Gate assignment | `src/RasHub.Web/Controllers/RasEndpoints` and `src/RasHub.Application/RasEndpoints/Services` |
| Blazor RAS endpoint administration | `src/RasHub.Web/Components/Pages/RasEndpoints.razor` and `src/RasHub.Web/Components/RasEndpointEditorDialog.razor` |
| Cluster shadow reads | `src/RasHub.Web/Controllers/RasClusters/RasGateClusterShadowController.cs` |
| Global cluster shadow search | `src/RasHub.Web/Controllers/RasClusters/RasClusterShadowSearchController.cs` |
| Live cluster reads and shadow refresh | `src/RasHub.Web/Controllers/RasClusters/RasGateClusterLiveController.cs` |
| Cluster create/update/remove | `src/RasHub.Web/Controllers/RasClusters/RasGateClusterAdministrationController.cs` |
| Infobase shadow reads | `src/RasHub.Web/Controllers/RasInfobases/RasClusterInfobaseShadowController.cs` |
| Global infobase shadow search | `src/RasHub.Web/Controllers/RasInfobases/RasInfobaseShadowSearchController.cs` |
| Live infobase reads and shadow refresh | `src/RasHub.Web/Controllers/RasInfobases/RasClusterInfobaseLiveController.cs` |
| Hosted Gate status monitoring | `src/RasHub.Web/Infrastructure/RasGates/RasGateMonitoringService.cs` |
| DI and handler registration | `src/RasHub.Web/Program.cs` and `src/RasHub.Infrastructure/Extensions/ServiceCollectionExtensions.cs` |

API controllers and Blazor administration services delegate lifecycle writes
to `RasGateRegistry` and `RasEndpointRegistry`.
Endpoint identity changes own resource-shadow invalidation; Gate changes own
only Gate-derived status. Authorization and presentation remain adapter-specific.
Every Endpoint has one required Gate assignment; one Gate may execute work for
multiple Endpoints.

## Real execution flows

### Persisted API query

```text
Controller -> [ActiveRasEndpointLookup for parent-scoped reads] -> Ras*Queries
           -> AsNoTracking EF projection -> Contracts response/model
           -> ApiResponse<T>
```

Endpoint-scoped cluster and infobase shadow queries use
`ActiveRasEndpointLookup`; top-level Gate/endpoint registration, Gate status,
and global search queries go directly to their Infrastructure query modules.
Global cluster and infobase searches read across the persisted shadow and may
filter by RAS endpoint and cluster identity. These persisted queries do not use
Application handlers and do not contact RasGate.

### Live read, shadow refresh, or remote mutation

```text
Controller -> ActiveRasEndpointLookup + optional parent/resource lookup
 -> InteractiveTaskRunner -> IBackgroundTaskEngine
 -> lane worker and per-endpoint concurrency key -> fresh attempt DI scope
 -> Application task handler -> RasEndpointExecutionTargetResolver
 -> endpoint + its assigned Gate; capture endpoint and Gate revisions
 -> resource gateway -> internal RasGate session -> RasGate
       -> RAC command with endpoint host:port appended as its final argument
 -> IRasEndpointSyncPublisher -> [cluster/infobase snapshot store]
 -> one SaveChangesAsync -> controller returns the endpoint-scoped response
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
shadow refreshes. A reachable Gate with `available: false` RAC is recorded as
degraded; a failed RAC probe records RAC as unknown without discarding the
successful Gate observation.

### Shadow publication and endpoint/Gate configuration

Handlers capture both the RAS endpoint and selected RasGate configuration
revisions before remote I/O. The resource publisher guards both revisions plus
active/deleted state, applies a complete collection or targeted change, updates
endpoint observation metadata, and saves once. Complete collections can
remove absent children; targeted upserts leave siblings unchanged. A definitive
targeted remote not-found soft-deletes only the requested shadow resource;
deleting a cluster also invalidates its cached infobases.

Status publication remains Gate-scoped and writes RasGate/RAC observations in
one guarded save. Endpoint host/port changes, deactivation, deletion, and
restoration invalidate endpoint-owned cluster/infobase shadow. Gate changes do
not discard that shadow, but their revision guard prevents in-flight work from
publishing through obsolete Gate configuration. Reassigning an Endpoint to a
different Gate advances the Endpoint revision for the same reason while
preserving its shadow because the remote RAS identity is unchanged.

Gate-write mechanics live under `Database/Interceptors` and
`EntityTypeConfigurations`. Remote keys are parent-scoped—clusters by RAS
endpoint and infobases by cluster—and are distinct from Hub IDs.

## Change routing

| Change | Follow through |
|---|---|
| Public request/response | Contracts submodule -> Web controller -> Infrastructure query projection for persisted reads -> OpenAPI and Contracts/Web tests |
| Remote-derived persisted field | Application snapshot -> RAC deserializer -> Domain entity -> EF mapping/migration -> snapshot store -> query contract -> tests |
| New live read or shadow refresh | Application gateway/task -> Infrastructure gateway/adapter/DI -> publisher/store when shadow state changes -> Web controller/task options/handler registration -> tests |
| Cluster mutation | Contract mapping -> task/options -> gateway/adapter -> read-back or guarded removal -> unknown-outcome and Web tests |
| RAC output-format-only change | Resource deserializer and resolver registration/tests; keep the command adapter |
| RAC command or outcome change | Operation adapter -> typed interface plus descriptor registration -> capability and gateway tests |
| Persistence invariant | Entity/configuration/interceptor/store/publisher/migration plus Infrastructure integration tests |
| Retry/dedup/concurrency policy | `src/RasHub.Web/Infrastructure/RasGates/RasGateTaskOptions.cs` plus handler/API tests |
| Generic engine mechanics | `RasHub.BackgroundTasks` plus its README and integration suite |
| Authentication or authorization | Web authentication/authorization, pipeline registration and Web integration tests |

## Detailed references

- [RAC compatibility boundary](rac-compatibility.md)
- [Background task engine](../src/RasHub.BackgroundTasks/README.md)
- [Test suites](../tests/README.md)
- [Deployment and health](../deploy/README.md)
