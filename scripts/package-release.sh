#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION_FILE="$ROOT_DIR/version.json"
SOURCE_COMPOSE="$ROOT_DIR/deploy/compose.production.yaml"
SOURCE_ENV="$ROOT_DIR/deploy/environments/.env.production.example"
SOURCE_README="$ROOT_DIR/deploy/README.release.md"
SOURCE_SETUP="$ROOT_DIR/deploy/setup.sh"
SOURCE_SETUP_TEST="$ROOT_DIR/scripts/test-release-setup.sh"
ARTIFACTS_DIR="$ROOT_DIR/artifacts/release"
IMAGE_REPOSITORY="ghcr.io/rasecosystem/ras-hub"

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "Error: required command '$1' was not found." >&2
        exit 1
    fi
}

read_version() {
    python3 -c '
import json
import re
import sys

with open(sys.argv[1], encoding="utf-8") as file:
    version = json.load(file).get("version")

pattern = r"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$"

if not isinstance(version, str) or re.fullmatch(pattern, version) is None:
    raise SystemExit("version.json does not contain a supported semantic version")

print(version)
' "$VERSION_FILE"
}

require_command python3

VERSION="$(read_version)"
IMAGE="$IMAGE_REPOSITORY:$VERSION"

if [[ $# -gt 1 ]]; then
    echo "Usage: $0 [--version|--image|--repository]" >&2
    exit 2
fi

case "${1:-}" in
    "")
        ;;
    --version)
        printf '%s\n' "$VERSION"
        exit 0
        ;;
    --image)
        printf '%s\n' "$IMAGE"
        exit 0
        ;;
    --repository)
        printf '%s\n' "$IMAGE_REPOSITORY"
        exit 0
        ;;
    *)
        echo "Usage: $0 [--version|--image|--repository]" >&2
        exit 2
        ;;
esac

require_command cp
require_command chmod
require_command docker
require_command git
require_command grep
require_command gzip
require_command mkdir
require_command rm
require_command sha256sum
require_command tar

for required_file in \
    "$SOURCE_COMPOSE" \
    "$SOURCE_ENV" \
    "$SOURCE_README" \
    "$SOURCE_SETUP" \
    "$SOURCE_SETUP_TEST" \
    "$ROOT_DIR/LICENSE"; do
    if [[ ! -f "$required_file" ]]; then
        echo "Error: release input was not found: $required_file" >&2
        exit 1
    fi
done

"$SOURCE_SETUP_TEST"

PACKAGE_NAME="rashub-$VERSION-deploy"
STAGE_DIR="$ARTIFACTS_DIR/.stage-$PACKAGE_NAME"
PACKAGE_DIR="$STAGE_DIR/$PACKAGE_NAME"
ARCHIVE_NAME="$PACKAGE_NAME.tar.gz"
ARCHIVE_PATH="$ARTIFACTS_DIR/$ARCHIVE_NAME"

trap 'rm -rf -- "$STAGE_DIR"' EXIT HUP INT TERM

rm -rf "$STAGE_DIR"
rm -f "$ARCHIVE_PATH" "$ARTIFACTS_DIR/SHA256SUMS"
mkdir -p "$PACKAGE_DIR"

cp "$SOURCE_COMPOSE" "$PACKAGE_DIR/compose.yaml"
cp "$SOURCE_README" "$PACKAGE_DIR/README.md"
cp "$SOURCE_SETUP" "$PACKAGE_DIR/setup.sh"
cp "$ROOT_DIR/LICENSE" "$PACKAGE_DIR/LICENSE"

python3 -c '
import sys

source, destination, image = sys.argv[1:]
lines = open(source, encoding="utf-8").read().splitlines()
replaced = False

for index, line in enumerate(lines):
    if line.startswith("RASHUB_IMAGE="):
        lines[index] = f"RASHUB_IMAGE={image}"
        replaced = True
        break

if not replaced:
    raise SystemExit("release environment template does not define RASHUB_IMAGE")

with open(destination, "w", encoding="utf-8", newline="\n") as file:
    file.write("\n".join(lines) + "\n")
' "$SOURCE_ENV" "$PACKAGE_DIR/.env.example" "$IMAGE"

chmod 0755 "$PACKAGE_DIR"
chmod 0755 "$PACKAGE_DIR/setup.sh"
chmod 0644 \
    "$PACKAGE_DIR/compose.yaml" \
    "$PACKAGE_DIR/.env.example" \
    "$PACKAGE_DIR/README.md" \
    "$PACKAGE_DIR/LICENSE"

docker compose \
    --env-file "$PACKAGE_DIR/.env.example" \
    --file "$PACKAGE_DIR/compose.yaml" \
    config --quiet

docker compose \
    --env-file "$PACKAGE_DIR/.env.example" \
    --file "$PACKAGE_DIR/compose.yaml" \
    config --format json |
    python3 -c '
import json
import sys

configuration = json.load(sys.stdin)
rashub = configuration["services"]["rashub"]
trusted_proxy = rashub["environment"]["ReverseProxy__KnownProxies__0"]
default_network = configuration["networks"]["default"]
ipam_configs = default_network.get("ipam", {}).get("config", [])

if len(ipam_configs) != 1:
    raise SystemExit("production Compose must define exactly one default network IPAM configuration")

gateway = ipam_configs[0].get("gateway")

if not gateway or trusted_proxy != gateway:
    raise SystemExit(
        "production Compose gateway must match the trusted reverse proxy address"
    )
'

docker compose \
    --env-file "$PACKAGE_DIR/.env.example" \
    --file "$PACKAGE_DIR/compose.yaml" \
    config --images |
    grep -Fxq "$IMAGE"

SOURCE_DATE_EPOCH="$(git -C "$ROOT_DIR" show -s --format=%ct HEAD)"

tar \
    --sort=name \
    --mtime="@$SOURCE_DATE_EPOCH" \
    --owner=0 \
    --group=0 \
    --numeric-owner \
    --create \
    --file=- \
    --directory "$STAGE_DIR" \
    "$PACKAGE_NAME" |
    gzip -n > "$ARCHIVE_PATH"

archive_entries="$(tar -tzf "$ARCHIVE_PATH")"

for entry in \
    compose.yaml \
    .env.example \
    setup.sh \
    README.md \
    LICENSE; do
    grep -Fxq "$PACKAGE_NAME/$entry" <<<"$archive_entries" || {
        echo "Error: release archive is missing $entry." >&2
        exit 1
    }
done

(
    cd "$ARTIFACTS_DIR"
    sha256sum "$ARCHIVE_NAME" > SHA256SUMS
)

echo "Release artifacts are ready:"
echo "  artifacts/release/$ARCHIVE_NAME"
echo "  artifacts/release/SHA256SUMS"
echo "Container image:"
echo "  $IMAGE"
