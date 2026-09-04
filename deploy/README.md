# Deployment

This document covers development and source-checkout operations. The deployment
archive contains its own [operator guide](README.release.md). Run
`make -C deploy help` for the available commands.

## Development

Start PostgreSQL and Seq for an application running from the IDE:

```bash
make dev-up
```

Run the complete stack at `http://127.0.0.1:5076` with `make dev-stack-up` and
stop it with `make dev-down`.

Defaults live in `environments/.env.development.example`. For a shared
development server, copy `environments/.env.development.server.example`,
replace its credentials, and pass the file as `DEV_ENV_FILE`. Keep PostgreSQL
private and set `SEQ_DEV_PUBLIC_URL` to the browser-visible Seq URL.

## Production from source

```bash
./deploy/setup.sh
```

The setup script generates the production secrets and environment file and
builds and starts the production stack, waits for readiness, and prints the
initial `rashub@rashub` and Seq administrator passwords. The source deployment
combines `compose.production.yaml` with `compose.production.build.yaml`; release
bundles use the same installer with a pinned prebuilt image.

Production assumptions:

- run one RasHub replica because background coordination is process-local;
- terminate TLS at a reverse proxy and keep the RasHub and Seq host bindings on
  localhost;
- do not expose Seq without separate access control;
- keep PostgreSQL off the host network;
- store bootstrap and Data Protection secrets in files outside the repository;
- back up PostgreSQL and Data Protection material together.

The Compose network assigns a stable gateway to the host reverse proxy. If
`RASHUB_DOCKER_SUBNET` overlaps another host, VPN, or Docker network, choose a
different private `/24` before the first start and keep
`RASHUB_DOCKER_GATEWAY` inside it. RasHub trusts forwarded headers only from
that gateway.

The one-shot `migrate` service updates both databases before the API starts.
Detailed secret creation, first-start, health, backup, and upgrade commands are
kept in the [container bundle guide](README.release.md).

Anonymous health endpoints:

- `/health/live` — the Web process is responding;
- `/health/ready` — the database and background runtime are ready.

## Data Protection and RasGate network

The persisted Data Protection key ring protects cookies, tokens, and stored
RasGate API keys. Production must encrypt it with the configured long-lived
X.509 certificate. Losing the ring, certificate, or password invalidates
sessions and requires replacing registered RasGate API keys.

RasGate endpoints may use HTTP or HTTPS. Use HTTPS outside a strictly isolated
network and restrict outbound container traffic to approved Gate addresses.

## Migrations and shutdown

```bash
make -C deploy migrations-add MIGRATION_NAME=Name
make -C deploy database-update
make -C deploy prod-down
```

## Delivery automation

Pushes to `dev` publish and deploy `dev-<commit-sha>`. Production releases are
tag-driven and documented in [docs/releasing.md](../docs/releasing.md).

The self-hosted development runner calls a root-owned helper. Reinstall it
after changing `deploy/scripts/rashub-dev-deploy`:

```bash
sudo install -o root -g root -m 0755 \
  deploy/scripts/rashub-dev-deploy \
  /usr/local/sbin/rashub-dev-deploy
```
