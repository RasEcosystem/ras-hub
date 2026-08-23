# RasHub

RasHub is the central .NET backend for
[RasStudio](https://github.com/RasEcosystem/ras-studio) and
[RasGate](https://github.com/RasEcosystem/ras-gate). Shared API models live in the
[`RasHub.Contracts`](src/RasHub.Contracts) submodule.

## Requirements

- .NET SDK 10
- Git and Make
- Docker with Compose for local services and deployment

## Build and test

```bash
git submodule update --init --recursive
make release
make test
```

Useful commands:

```bash
make publish            # self-contained linux-x64 build
make submodules-update  # update submodule revisions
make help               # all root commands
```

## Development

Start PostgreSQL and Seq, then run `RasHub.Web` with its Development launch
profile:

```bash
make dev-up
```

Use `make dev-stack-up` to run the complete stack in containers and
`make dev-down` to stop it. See [deploy/README.md](deploy/README.md) for
production setup and migrations.

In Development, authenticated API documentation is available at `/swagger`.

## Architecture

RasHub keeps a local shadow of remote 1C:Enterprise infrastructure. Shadow
endpoints read that persisted state without contacting RasGate. Live reads,
explicit shadow refresh, hosted status monitoring, and remote mutations use
the in-process background task engine.

```text
RasStudio / Blazor / API
          |
       RasHub.Web
       /       \
shadow query  live / refresh / mutation / monitoring
     |                         |
query service         BackgroundTasks engine
     |                         |
  EF Core            Application task handler
     |                     /             \
PostgreSQL          resource gateway   shadow publisher
                           |                 |
                     RasGate session      EF Core
                           |                 |
                   RasGate -> RAC -> RAS  PostgreSQL
```

- `RasHub.Domain` contains Hub-owned persisted entities.
- `RasHub.Application` contains normalized remote models, background handlers,
  and the status, cluster, and infobase gateway contracts.
- `RasHub.Infrastructure` implements those gateways, EF Core persistence, and
  version-aware RAC adapters. A per-Gate session owns the common HTTP envelope,
  endpoint, authentication, RAC-version handling, and error semantics.
- `RasHub.BackgroundTasks` is generic in-process execution machinery; its
  queues, schedules, deduplication, and concurrency keys are not durable or
  distributed.
- `RasHub.Contracts` contains the versioned wire models shared with API clients
  and has no dependency on server implementation projects.
- `RasHub.Web` owns HTTP, Blazor, Identity, monitoring, and process composition.

The current remote boundary supports aggregate RasGate/RAC status, cluster
snapshot and administration operations, and cluster-scoped infobase snapshot
and detail reads. Complete collection snapshots may remove missing shadow
rows; targeted live reads update only the requested resource. Every remote
publication is guarded by the captured RasGate configuration revision. Hosted
monitoring refreshes the aggregate RasGate/RAC status only; cluster and
infobase shadow state is updated through live reads or explicit refresh
commands.

## API

The versioned HTTP surface is under `/api/v1` and returns the shared
`ApiResponse<T>` envelope. API controllers authenticate the user-owned
`X-Api-Key`. RasGate configuration writes and remote cluster mutations also
require the `ManageRasGates` policy, currently granted to administrators.
Shadow queries never contact RasGate. Live reads and explicit shadow refresh
commands enqueue remote work, publish the validated result to the shadow, and
await the in-process task handle before responding.

Global search for RasGate registrations, clusters, and infobases runs only
against persisted state. Cluster and infobase search results include their
parent context and can be narrowed by the corresponding parent identifiers.

The shadow Gate status reports RasGate identity/version and RAC
availability/version. Its state is `Unknown`, `Offline`, `Degraded`, or `Ready`;
a reachable RasGate with unavailable or unobservable RAC is degraded rather
than reported as fully ready.

## Internals

- [Code map and execution flows](docs/code-map.md)
- [Background task engine](src/RasHub.BackgroundTasks/README.md)
- [Test suites](tests/README.md)
- [RAC compatibility boundary](docs/rac-compatibility.md)
