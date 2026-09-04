# Releasing RasHub

RasHub releases contain a Linux AMD64 image and a small deployment archive.
Standalone binaries are not published.

## Repository setup

- Allow GitHub Actions to create releases and write organization packages.
- Connect the `RasEcosystem/ras-hub` GHCR package to this repository and grant
  its workflows write access.
- Make the package public before the first public release.
- Protect `main` and require the `verify` check from
  `.github/workflows/verify.yaml` on pull requests.

Never put registry credentials, database passwords, bootstrap credentials, or
Data Protection material in a tag, image, release asset, or release note.

## Prepare the version

`version.json` is the single release-version source. It drives assembly and Web
versions, tag validation, the image tag, and the archive name. Use a SemVer
prerelease suffix while behavior is unstable, for example:

```json
"version": "0.1.0-beta.2"
```

Update it in a reviewed branch and run:

```bash
make release
```

This checks formatting, performs a warning-free Release build, runs all tests,
validates production Compose and archive contents, and writes the ignored
artifacts under `artifacts/release`.

## Tag and publish

Create one annotated tag on the reviewed commit after it reaches `main`. The
tag must be `v` followed by the exact value from `version.json`:

```bash
git switch main
git pull --ff-only
git tag -a v0.1.0-beta.2 -m "RasHub 0.1.0-beta.2"
git push origin v0.1.0-beta.2
```

The workflow repeats verification, publishes
`ghcr.io/rasecosystem/ras-hub:<version>` for `linux/amd64`, attaches SBOM and
provenance, and creates the GitHub release with the deployment archive and
`SHA256SUMS`. Stable versions additionally update `latest`.

Never move, reuse, or rebuild a published version tag. The current workflow
pushes the image before creating the GitHub release, so do not rerun it after an
image was published. If publication fails after that point, inspect the pushed
digest and issue the next version instead of overwriting the existing tag.

## Verify publication

From a host not authenticated to GHCR:

```bash
sha256sum --check SHA256SUMS
docker pull ghcr.io/rasecosystem/ras-hub:0.1.0-beta.2
docker buildx imagetools inspect \
  ghcr.io/rasecosystem/ras-hub:0.1.0-beta.2
```

Confirm that the registry digest matches the digest in the release notes and
that `.env.example` in the archive uses the same versioned image tag. Deploy
the archive to a disposable environment before using persistent production
data.
