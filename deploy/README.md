# Deployment

All database and container operations are isolated in this directory.

## Branch and deployment flow

Development uses a permanent `dev` integration branch:

```text
feature/* -> pull request -> dev -> development image -> development server
                                      |
                                      +-> ghcr.io/<owner>/<repository>:dev-<commit-sha>
```

Feature branches do not deploy automatically. Every push or merge to `dev`
builds an immutable image tagged with the full commit SHA. A successful image
build triggers the development deployment workflow, which deploys that exact
SHA to the `development` GitHub environment.

The deployment workflow can also be started manually with a full 40-character
commit SHA, provided that the corresponding `dev-<sha>` image already exists.
Pushes to `main` do not deploy anything yet; production automation will be
added separately.

## Development

Start PostgreSQL and Seq for local development:

```bash
make dev-up
```

PostgreSQL is available at `127.0.0.1:5432`. Run RasHub from the IDE using a
Development launch profile; its connection string is already configured for
the local PostgreSQL instance.

To verify the complete development stack in containers, run:

```bash
make dev-stack-up
```

The containerized API is available at `http://127.0.0.1:5076`. The tracked
development environment contains local-only defaults. Seq is available at
`http://127.0.0.1:5341` (`admin` / `rashub`). To override them, copy
`deploy/environments/.env.development.example` to an ignored file and pass it
as `DEV_ENV_FILE`.

For a development server behind a reverse proxy, set
`RASHUB_DEV_BIND_ADDRESS` in that ignored environment file to the server's
private network address. Set `SEQ_DEV_PUBLIC_URL` to the browser-accessible
Seq URL (preferably an HTTPS reverse-proxy address); Docker's internal `seq`
hostname cannot be used by the browser. Keep PostgreSQL bound to localhost.

```bash
make dev-down
```

## Production

Create the ignored production environment and replace every secret:

```bash
cp deploy/environments/.env.production.example \
   deploy/environments/.env.production
make -C deploy prod-up
```

The one-shot `migrate` service applies migrations after PostgreSQL becomes
healthy. The API starts only after migration completion. PostgreSQL isn't
published to the host, and the API binds to `127.0.0.1:8080` by default for a
TLS reverse proxy.

Seq is bound to `127.0.0.1:5341` by default and must be published through the
TLS reverse proxy at `SEQ_PUBLIC_URL`. Before the first production start,
generate the required administrator password hash:

```bash
printf '%s' 'replace-with-a-long-random-password' |
  docker run --rm -i datalust/seq:2026.1 config hash
```

Store the resulting value in `SEQ_ADMIN_PASSWORD_HASH`. Seq configuration and
events are persisted in the `seq-production` volume. The admin-only navigation
button opens `SEQ_PUBLIC_URL`; Seq still enforces its own authentication.

## Migrations

```bash
make -C deploy migrations-add MIGRATION_NAME=InitialCreate
make -C deploy database-update
```

Run `make -C deploy help` for the complete command list.
