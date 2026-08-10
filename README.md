# RasHub

RasHub is the central .NET backend for
[RasStudio](https://github.com/zmaxb/ras-studio) and
[RasGate](https://github.com/zmaxb/ras-gate). Shared API models live in the
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

## Internals

- [Background task engine](src/RasHub.BackgroundTasks/README.md)
- [Test suites](tests/README.md)
- [RAC compatibility boundary](docs/rac-compatibility.md)
