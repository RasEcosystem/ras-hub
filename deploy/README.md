# Deployment

All database and container operations are isolated in this directory.

## Development

Start PostgreSQL for local development:

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
development environment contains local-only defaults. To override them, copy
`deploy/environments/.env.development.example` to an ignored file and pass it
as `DEV_ENV_FILE`.

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

## Migrations

```bash
make -C deploy migrations-add MIGRATION_NAME=InitialCreate
make -C deploy database-update
```

Run `make -C deploy help` for the complete command list.
