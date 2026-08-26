# Releasing RasHub

RasHub releases are immutable Linux AMD64 container images accompanied by a
small deployment bundle. Standalone Linux and Windows binaries are not release
artifacts.

## One-time repository setup

- Allow GitHub Actions to create releases and write organization packages.
- In the `RasEcosystem/ras-hub` GHCR package settings, connect the package to
  this repository and grant its workflows write access.
- Change the GHCR package visibility to **Public** before the first public
  release. GitHub does not allow a public package to become private again.
- Protect `main` and require the normal verification checks before merging.

Never put registry credentials, PostgreSQL passwords, bootstrap credentials, or
Data Protection material in a tag, release asset, image layer, or release note.

## Prepare a version

The release version is committed in `version.json`. The Git tag records that
decision; it does not override it. Use a SemVer prerelease suffix while the API
or operational behavior remains unstable, for example:

```json
"version": "0.1.0-beta.1"
```

This value drives the assembly, Web display, authenticated `/api/v1/info`
response, release tag validation, container tag, and deployment archive name.
Do not maintain a separate release-stage setting or hard-coded UI badge.

Update the version in the feature or release-preparation branch, run the local
release verification, and merge the reviewed commit into `main`:

```bash
make release
```

This runs formatting verification, a warning-free Release build, all tests,
deployment Compose validation, archive-content validation, and checksum
generation. Generated files remain under `artifacts/release` and are ignored by
Git.

## Tag and publish

Create an annotated tag on the exact reviewed commit in `main`. The tag must be
`v` followed by the exact version from `version.json`:

```bash
git switch main
git pull --ff-only
git tag -a v0.1.0-beta.1 -m "RasHub 0.1.0-beta.1"
git push origin v0.1.0-beta.1
```

Do not move or reuse a published release tag. Fix the problem and publish the
next version instead.

The release workflow verifies that the tag matches `version.json` and belongs
to `main`, then:

1. repeats formatting, build, tests, and packaging;
2. pushes `ghcr.io/rasecosystem/ras-hub:<version>` for `linux/amd64`;
3. attaches SBOM and provenance attestations to the image;
4. creates the GitHub release with the deployment archive and `SHA256SUMS`.

Prerelease versions are marked as GitHub prereleases and never update `latest`.
A stable version additionally updates `latest`.

## Verify the publication

Verify the workflow, assets, checksum, and anonymous image access from a host
that is not logged in to GHCR:

```bash
sha256sum --check SHA256SUMS
docker pull ghcr.io/rasecosystem/ras-hub:0.1.0-beta.1
```

Extract the deployment bundle and confirm that `.env.example` pins the same
image version. Perform the first deployment in a disposable environment before
promoting it to a server holding persistent data.
