# RasHub container deployment

This guide is shipped in the deployment archive. The archive pins a versioned
RasHub image tag and contains no credentials, certificates, database data, or
Data Protection keys.

## Requirements

- Linux x86-64 with Docker Engine and Docker Compose v2;
- OpenSSL and `sudo` access, or a root shell for secret-file preparation;
- a TLS reverse proxy in front of the locally bound RasHub port;
- one or more RasGate endpoints with access to compatible RAC and RAS;
- persistent storage and an off-host backup destination.

Run one RasHub replica. Keep Seq local or protect it independently; do not
publish its UI or ingestion API anonymously.

The TLS proxy must forward the original host, client address, and protocol and
support WebSocket upgrade for the Blazor Server connection.

## First deployment

Run the installer from the extracted release directory:

```bash
./setup.sh
```

Running `sh setup.sh` is also supported; the script switches to Bash itself.

The installer creates `.env`, the `rashub-secrets` group, the bootstrap and
Data Protection files, and random PostgreSQL and Seq credentials. It validates
the Compose configuration, downloads the pinned images, runs database
migrations, starts the complete stack, and waits until RasHub is healthy.

After a successful first installation it prints the local address, the RasHub
login `rashub@rashub`, and the generated RasHub and Seq passwords. Store them
in a password manager. The application logger and Seq never receive these
passwords.

Existing `.env` files and secrets are never overwritten. Running `setup.sh`
again validates and starts the existing stack without changing or displaying
its credentials. Use `--secrets-dir` or `--secret-group` only when the host
requires different locations or ownership.

RasHub and Seq remain bound to localhost by default. Configure the host TLS
reverse proxy before exposing either service. After that, verify readiness
through the public RasHub endpoint:

```bash
curl --fail https://rashub.example.com/health/ready
```

The bootstrap account is created only when the database contains no
administrator. Later restarts never reset an existing administrator or its
password.

## Backup, upgrade, and rollback

Before every upgrade, back up PostgreSQL, the Data Protection volume, its
certificate, and its password to storage outside this host. A database dump can
be streamed with:

```bash
docker compose --env-file .env --file compose.yaml exec -T postgres \
  sh -c 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB"' > rashub.sql
```

Losing Data Protection material invalidates sessions and makes stored RasGate
API keys unreadable.

Set `RASHUB_IMAGE` to the new versioned tag, then run `pull` and `up --detach`.
To roll back, restore the previous image tag. If the upgrade applied an
incompatible migration, restore the matching PostgreSQL backup as well.

Never deploy the mutable `latest` tag when an exact release version is
available.
