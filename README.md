[English](README.md) | [Русский](README.ru.md)

# RasHub

RasHub is the central management service for
[RasStudio Mono](https://github.com/RasEcosystem/ras-studio-mono). It exposes a
versioned API, stores a local shadow of 1C:Enterprise infrastructure, and sends
RAC operations to one or more
[RasGate](https://github.com/RasEcosystem/ras-gate) instances. Shared API models
live in the [`RasHub.Contracts`](src/RasHub.Contracts) submodule.

## Current scope

- Blazor administration UI and `/api/v1` HTTP API;
- persisted RasGate, RAS endpoint, cluster, infobase, and status shadow state;
- explicit live reads, shadow refresh, and cluster administration through RAC;
- version-aware RAC adapters with `8.3.27.2214` as the current minimum;
- in-process background work, supporting one RasHub replica.

Shadow reads do not contact RasGate. Resource operations address a managed RAS
endpoint and execute through its assigned active RasGate. Remote results are
published only while both endpoint and assigned Gate revisions remain current.

## Requirements

- .NET SDK 10;
- Git and Make;
- Docker Engine with Compose v2.

Remote management additionally requires RasGate with network access to the
configured RAS endpoints and a compatible RAC installation.

## Build and development

```bash
git submodule update --init --recursive
make build
make test
```

Start PostgreSQL and Seq for an IDE-run application with `make dev-up`. Use
`make dev-stack-up` for the complete container stack and `make dev-down` to
stop it. Authenticated API documentation is available at `/swagger` in the
Development environment.

Run `make help` for all root commands and `make -C deploy help` for deployment
and migration commands.

## Releases

RasHub is distributed as a Linux AMD64 container with a small deployment
bundle. Before preparing a release tag, run:

```bash
make release
```

The command verifies formatting, performs a warning-free Release build, runs
all tests, and validates the deployment archive. See the
[release procedure](docs/releasing.md) for tag and publication rules.

## Documentation

- [Local and source deployments](deploy/README.md)
- [Container bundle deployment](deploy/README.release.md)
- [Code map and execution flows](docs/code-map.md)
- [RAC compatibility](docs/rac-compatibility.md)
- [Background task engine](src/RasHub.BackgroundTasks/README.md)
- [Test suites](tests/README.md)

## Related projects

RasHub is part of the [Ras Ecosystem](https://github.com/RasEcosystem):

- [RasGate](https://github.com/RasEcosystem/ras-gate) executes controlled RAC
  commands for RasHub;
- [RasStudio Mono](https://github.com/RasEcosystem/ras-studio-mono) is the
  administration client built on the RasHub API.

## License

RasHub is licensed under the [MIT License](LICENSE).
