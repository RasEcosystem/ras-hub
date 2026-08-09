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

RasHub creates its first administrator from
`RASHUB_DEV_BOOTSTRAP_ADMIN_EMAIL` and
`RASHUB_DEV_BOOTSTRAP_ADMIN_PASSWORD`. The Compose defaults are local-only;
replace them on a shared development server. Public account registration is
disabled by default and can be enabled later by an administrator.

For a development server behind a reverse proxy, set
`RASHUB_DEV_BIND_ADDRESS` in that ignored environment file to the server's
private network address. Set `SEQ_DEV_PUBLIC_URL` to the browser-accessible
Seq URL (preferably an HTTPS reverse-proxy address); Docker's internal `seq`
hostname cannot be used by the browser. Keep PostgreSQL bound to localhost.

The container stores its ASP.NET Core Data Protection key ring in the
`data-protection-development` volume so authentication cookies and antiforgery
tokens survive image updates. Server deployments should also encrypt that key
ring with a long-lived X.509 certificate. Create it once on the server:

```bash
sudo install -d -m 700 /opt/rashub-dev/secrets
sudo openssl rand -base64 -out /opt/rashub-dev/secrets/data-protection-password 48
sudo openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes \
  -subj "/CN=RasHub Development Data Protection" \
  -keyout /opt/rashub-dev/secrets/data-protection.key \
  -out /opt/rashub-dev/secrets/data-protection.crt
sudo openssl pkcs12 -export \
  -out /opt/rashub-dev/secrets/data-protection.pfx \
  -inkey /opt/rashub-dev/secrets/data-protection.key \
  -in /opt/rashub-dev/secrets/data-protection.crt \
  -passout file:/opt/rashub-dev/secrets/data-protection-password
sudo chmod 600 /opt/rashub-dev/secrets/data-protection.pfx \
  /opt/rashub-dev/secrets/data-protection-password
sudo rm /opt/rashub-dev/secrets/data-protection.key \
  /opt/rashub-dev/secrets/data-protection.crt
```

Set `RASHUB_DEV_DATA_PROTECTION_CERTIFICATE_PATH` and
`RASHUB_DEV_DATA_PROTECTION_CERTIFICATE_PASSWORD_FILE` in
`/opt/rashub-dev/.env.development` as shown in
`.env.development.server.example`. Keep both files backed up outside the
server. They must remain unchanged between deployments.

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

Before the first start, create a password file for the RasHub bootstrap
administrator and keep it readable only by the deployment account:

```bash
sudo install -d -m 700 /opt/rashub/secrets
sudo openssl rand -base64 -out /opt/rashub/secrets/bootstrap-admin-password 32
sudo chmod 600 /opt/rashub/secrets/bootstrap-admin-password
```

Set `RASHUB_BOOTSTRAP_ADMIN_EMAIL` and
`RASHUB_BOOTSTRAP_ADMIN_PASSWORD_FILE` in `.env.production`. On an empty
Identity database RasHub creates this account and assigns the `Admin` role
before accepting requests. On later starts the bootstrap is a no-op once an
administrator exists. Keep the password file secret; it is never logged.

RasGate endpoints accept HTTP or HTTPS URLs and any valid TCP port. RasHub
disables HTTP redirects and bypasses system HTTP proxies. Prefer HTTPS whenever
RasGate supports it and enforce any required outbound restrictions at the host
or container network boundary.

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

Production also requires a long-lived certificate for encrypting its persistent
ASP.NET Core Data Protection key ring. Create it once using the development
commands above, replacing `/opt/rashub-dev/secrets` with
`/opt/rashub/secrets` and using a production-specific subject. Set
`RASHUB_DATA_PROTECTION_CERTIFICATE_PATH` and
`RASHUB_DATA_PROTECTION_CERTIFICATE_PASSWORD_FILE` in `.env.production`.

Back up the certificate, its password, and the `data-protection-production`
volume. Don't remove that volume during routine deployments. Losing either the
key ring or the certificate invalidates authentication cookies, antiforgery
tokens, password reset tokens, and other protected application data. The first
deployment of this configuration can't recover keys already lost with an old
container, so existing browser sessions may need their site cookies cleared
once.

## Migrations

```bash
make -C deploy migrations-add MIGRATION_NAME=InitialCreate
make -C deploy database-update
```

Run `make -C deploy help` for the complete command list.
