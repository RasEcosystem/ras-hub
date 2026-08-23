# RasHub container deployment

This bundle deploys the RasHub version pinned in `.env.example`. It does not
contain credentials, certificates, database data, or Data Protection keys.

## Requirements

- Linux x86-64 with Docker Engine and Docker Compose v2;
- a TLS reverse proxy in front of the locally bound RasHub and Seq ports;
- persistent storage and a backup destination outside the Docker host.

RasHub background coordination is process-local. Run one RasHub replica unless
distributed coordination and durable task recovery have been implemented.

## First deployment

1. Copy `.env.example` to `.env` and replace every placeholder.
2. Create the bootstrap administrator password and Data Protection files
   referenced by `.env`:

   ```bash
   sudo install -d -m 700 /opt/rashub/secrets
   sudo openssl rand -base64 \
     -out /opt/rashub/secrets/bootstrap-admin-password 32
   sudo openssl rand -base64 \
     -out /opt/rashub/secrets/data-protection-password 48
   sudo openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes \
     -subj "/CN=RasHub Data Protection" \
     -keyout /opt/rashub/secrets/data-protection.key \
     -out /opt/rashub/secrets/data-protection.crt
   sudo openssl pkcs12 -export \
     -out /opt/rashub/secrets/data-protection.pfx \
     -inkey /opt/rashub/secrets/data-protection.key \
     -in /opt/rashub/secrets/data-protection.crt \
     -passout file:/opt/rashub/secrets/data-protection-password
   sudo chmod 600 /opt/rashub/secrets/*
   ```

   Generate the Seq administrator password hash as documented by Seq and put
   only the resulting hash in `.env`.
3. Pull and start the pinned image:

   ```bash
   docker compose --env-file .env --file compose.yaml pull
   docker compose --env-file .env --file compose.yaml up --detach
   ```

The one-shot `migrate` service must complete successfully before RasHub starts.
Check readiness through the TLS endpoint exposed by the reverse proxy:

```bash
curl --fail https://rashub.example.com/health/ready
```

## Upgrade and rollback

Back up PostgreSQL, the Data Protection volume, its certificate, and its
password before every upgrade. A database backup can be streamed from the
running stack:

```bash
docker compose --env-file .env --file compose.yaml exec -T postgres \
  sh -c 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB"' > rashub.sql
```

To upgrade, replace `RASHUB_IMAGE` in `.env` with the immutable image tag from
the new release, then run `pull` and `up --detach` again. To roll back the
application, restore the previous image tag. If the new release applied a
non-backward-compatible migration, restore the matching PostgreSQL backup as
well.

Never use the mutable `latest` tag for a prerelease deployment.
