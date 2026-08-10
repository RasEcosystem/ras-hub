# Deployment

Run `make -C deploy help` for all deployment and migration commands.

## Development

Start PostgreSQL and Seq for an IDE-run application:

```bash
make dev-up
```

Run the complete container stack at `http://127.0.0.1:5076`:

```bash
make dev-stack-up
make dev-down
```

Local defaults live in `environments/.env.development.example`. For a shared
server, copy `environments/.env.development.server.example`, replace its
credentials, and pass the file as `DEV_ENV_FILE`. Keep PostgreSQL private and
set `SEQ_DEV_PUBLIC_URL` to the browser-accessible Seq URL.

## Production

Create the ignored environment file and replace every placeholder:

```bash
cp deploy/environments/.env.production.example \
   deploy/environments/.env.production
make -C deploy prod-up
```

The one-shot `migrate` container updates both databases before the API starts.
The API and Seq bind to localhost by default for publication through a TLS
reverse proxy; PostgreSQL is not published.

The container stack exports the `RasHub.BackgroundTasks` meter to Seq through
OTLP. Override `RASHUB_OTLP_METRICS_ENDPOINT` only when using another collector.

Create the bootstrap administrator password file once:

```bash
sudo install -d -m 700 /opt/rashub/secrets
sudo openssl rand -base64 -out /opt/rashub/secrets/bootstrap-admin-password 32
sudo chmod 600 /opt/rashub/secrets/bootstrap-admin-password
```

Set `RASHUB_BOOTSTRAP_ADMIN_EMAIL` and
`RASHUB_BOOTSTRAP_ADMIN_PASSWORD_FILE`. Bootstrap is a no-op after an
administrator exists.

Generate the required Seq administrator password hash:

```bash
printf '%s' 'replace-with-a-long-random-password' |
  docker run --rm -i datalust/seq:2026.1 config hash
```

Store it in `SEQ_ADMIN_PASSWORD_HASH`.

## Data Protection

RasHub persists its ASP.NET Core Data Protection key ring. It protects cookies,
tokens, and stored RasGate API keys. Shared and production deployments must also
encrypt the ring with a long-lived X.509 certificate:

```bash
sudo install -d -m 700 /opt/rashub/secrets
sudo openssl rand -base64 -out /opt/rashub/secrets/data-protection-password 48
sudo openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes \
  -subj "/CN=RasHub Data Protection" \
  -keyout /opt/rashub/secrets/data-protection.key \
  -out /opt/rashub/secrets/data-protection.crt
sudo openssl pkcs12 -export \
  -out /opt/rashub/secrets/data-protection.pfx \
  -inkey /opt/rashub/secrets/data-protection.key \
  -in /opt/rashub/secrets/data-protection.crt \
  -passout file:/opt/rashub/secrets/data-protection-password
sudo chmod 600 /opt/rashub/secrets/data-protection.pfx \
  /opt/rashub/secrets/data-protection-password
```

Set `RASHUB_DATA_PROTECTION_CERTIFICATE_PATH` and
`RASHUB_DATA_PROTECTION_CERTIFICATE_PASSWORD_FILE`. Development uses the same
names with the `RASHUB_DEV_` prefix.

Back up the certificate, password, and Data Protection volume together. Losing
them invalidates sessions and requires replacing every registered RasGate API
key.

## Network and migrations

RasGate URLs may use HTTP or HTTPS and any valid TCP port. Prefer HTTPS and
enforce outbound restrictions at the host or container boundary.

```bash
make -C deploy migrations-add MIGRATION_NAME=Name
make -C deploy database-update
make -C deploy prod-down
```

## Development CI

Pushes to `dev` run formatting, a warning-free Release build, and all tests,
then publish and deploy the immutable `dev-<commit-sha>` image. Production is
manual; pushes to `main` do not deploy it.
