# RasHub

RasHub is the central .NET backend for [RasStudio](https://github.com/zmaxb/ras-studio) and [RasGate](https://github.com/zmaxb/ras-gate). It acts as a hub for shared data and backend APIs.

Shared request and response models are provided by [RasHub.Contracts](https://github.com/zmaxb/ras-hub-contracts), included as a Git submodule.

## Requirements

- .NET SDK 10.0 or later
- Git
- Make

## Build

Initialize submodules and build all projects in Release mode:

```bash
make release
```

Update submodules to their latest configured revisions:

```bash
make submodules-update
```

Publish RasHub.Web as a self-contained Linux x64 single file:

```bash
make publish
```

Run `make help` to see all available commands.

## Deployment

Docker, Compose, database migration, development environment, and production
operations live in [`deploy`](deploy/README.md).

```bash
make -C deploy help
```
