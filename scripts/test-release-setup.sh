#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TEMPORARY_DIR="$(mktemp -d)"
trap 'rm -rf -- "$TEMPORARY_DIR"' EXIT HUP INT TERM

PACKAGE_DIR="$TEMPORARY_DIR/package"
FAKE_BIN_DIR="$TEMPORARY_DIR/bin"
SECRETS_DIR="$TEMPORARY_DIR/secrets"
OUTPUT_FILE="$TEMPORARY_DIR/setup-output"
DOCKER_LOG="$TEMPORARY_DIR/docker-log"
FAILURE_PACKAGE_DIR="$TEMPORARY_DIR/failure-package"
FAILURE_SECRETS_DIR="$TEMPORARY_DIR/failure-secrets"
FAILURE_OUTPUT_FILE="$TEMPORARY_DIR/failure-setup-output"
FAILURE_DOCKER_LOG="$TEMPORARY_DIR/failure-docker-log"
SOURCE_PACKAGE_DIR="$TEMPORARY_DIR/source-package"
SOURCE_SECRETS_DIR="$TEMPORARY_DIR/source-secrets"
SOURCE_OUTPUT_FILE="$TEMPORARY_DIR/source-setup-output"
SOURCE_DOCKER_LOG="$TEMPORARY_DIR/source-docker-log"
REAL_INSTALL="$(command -v install)"
export REAL_INSTALL

mkdir -p "$PACKAGE_DIR" "$FAKE_BIN_DIR"
cp "$ROOT_DIR/deploy/setup.sh" "$PACKAGE_DIR/setup.sh"
cp "$ROOT_DIR/deploy/compose.production.yaml" "$PACKAGE_DIR/compose.yaml"
cp "$ROOT_DIR/deploy/environments/.env.production.example" \
    "$PACKAGE_DIR/.env.example"
chmod 0755 "$PACKAGE_DIR/setup.sh"

cat > "$FAKE_BIN_DIR/docker" <<'EOF'
#!/usr/bin/env sh
set -eu

printf '%s\n' "$*" >> "$FAKE_DOCKER_LOG"

if [ "${1:-}" = "run" ]; then
    if [ "$#" -ne 6 ] ||
       [ "$2" != "--rm" ] ||
       [ "$3" != "-i" ] ||
       [ "$5" != "config" ] ||
       [ "$6" != "hash" ]; then
        echo "Unexpected docker run invocation" >&2
        exit 1
    fi

    cat >/dev/null
    printf '%s\n' 'PH:test-seq-password-hash'
    exit 0
fi

if [ "${1:-}" = "inspect" ]; then
    case "$*" in
        *State.Status*)
            printf '%s\n' 'running'
            ;;
        *State.Health*)
            printf '%s\n' "${FAKE_DOCKER_HEALTH:-healthy}"
            ;;
        *)
            echo "Unexpected docker inspect invocation" >&2
            exit 1
            ;;
    esac

    exit 0
fi

if [ "${1:-}" = "compose" ]; then
    case " $* " in
        *" ps --all -q rashub "*)
            printf '%s\n' 'fake-rashub-container'
            ;;
    esac

    exit 0
fi

echo "Unexpected docker invocation" >&2
exit 1
EOF
chmod 0755 "$FAKE_BIN_DIR/docker"

cat > "$FAKE_BIN_DIR/install" <<'EOF'
#!/usr/bin/env sh
set -eu

destination=""

for argument do
    destination="$argument"
done

"$REAL_INSTALL" "$@"

if [ -n "${FAKE_INSTALL_FAIL_DESTINATION:-}" ] &&
   [ "$destination" = "$FAKE_INSTALL_FAIL_DESTINATION" ]; then
    echo "Injected environment installation failure" >&2
    exit 73
fi
EOF
chmod 0755 "$FAKE_BIN_DIR/install"

FAKE_DOCKER_LOG="$DOCKER_LOG" \
PATH="$FAKE_BIN_DIR:$PATH" \
    sh "$PACKAGE_DIR/setup.sh" \
    --secrets-dir "$SECRETS_DIR" \
    --secret-group "$(id -gn)" \
    > "$OUTPUT_FILE"

test -f "$PACKAGE_DIR/.env"
test "$(stat -c '%a' "$PACKAGE_DIR/.env")" = "600"
test "$(stat -c '%a' "$SECRETS_DIR/bootstrap-admin-password")" = "640"
test "$(stat -c '%a' "$SECRETS_DIR/data-protection-password")" = "640"
test "$(stat -c '%a' "$SECRETS_DIR/data-protection.pfx")" = "640"

grep -Fxq 'RASHUB_BOOTSTRAP_ADMIN_EMAIL=rashub@rashub' "$PACKAGE_DIR/.env"
grep -Fxq "RASHUB_SECRET_GID=$(id -g)" "$PACKAGE_DIR/.env"
grep -Fxq "RASHUB_BOOTSTRAP_ADMIN_PASSWORD_FILE=$SECRETS_DIR/bootstrap-admin-password" \
    "$PACKAGE_DIR/.env"
grep -Fxq 'SEQ_PUBLIC_URL=http://127.0.0.1:5341/' "$PACKAGE_DIR/.env"
grep -Fxq 'SEQ_ADMIN_PASSWORD_HASH=PH:test-seq-password-hash' "$PACKAGE_DIR/.env"

admin_password="$(
    sed -n 's/^  Password: //p' "$OUTPUT_FILE" |
        sed -n '1p'
)"
stored_admin_password="$(tr -d '\r\n' < "$SECRETS_DIR/bootstrap-admin-password")"

test -n "$admin_password"
test "$admin_password" = "$stored_admin_password"
grep -Fq 'RasHub is running and healthy.' "$OUTPUT_FILE"
grep -Fq 'Local address: http://127.0.0.1:8080' "$OUTPUT_FILE"
grep -Fxq 'compose version' "$DOCKER_LOG"
grep -Fq ' config --quiet' "$DOCKER_LOG"
grep -Fq ' pull' "$DOCKER_LOG"
grep -Fq ' up --detach --remove-orphans' "$DOCKER_LOG"
grep -Fq ' ps --all -q rashub' "$DOCKER_LOG"
grep -Fq 'inspect --format {{.State.Status}} fake-rashub-container' "$DOCKER_LOG"

environment_checksum="$(sha256sum "$PACKAGE_DIR/.env")"
secret_checksum="$(sha256sum "$SECRETS_DIR/bootstrap-admin-password")"

FAKE_DOCKER_LOG="$DOCKER_LOG" \
PATH="$FAKE_BIN_DIR:$PATH" \
    "$PACKAGE_DIR/setup.sh" \
    --secrets-dir "$SECRETS_DIR" \
    --secret-group "$(id -gn)" \
    > "$OUTPUT_FILE"

test "$environment_checksum" = "$(sha256sum "$PACKAGE_DIR/.env")"
test "$secret_checksum" = "$(sha256sum "$SECRETS_DIR/bootstrap-admin-password")"
grep -Fq 'Using the existing RasHub configuration.' "$OUTPUT_FILE"
grep -Fq 'RasHub is running and healthy.' "$OUTPUT_FILE"
grep -Fq 'Existing administrator credentials were not changed.' "$OUTPUT_FILE"
! grep -Fq '  Password:' "$OUTPUT_FILE"
test "$(grep -Fc ' up --detach --remove-orphans' "$DOCKER_LOG")" = "2"

if FAKE_DOCKER_LOG="$DOCKER_LOG" \
   FAKE_DOCKER_HEALTH="unhealthy" \
   PATH="$FAKE_BIN_DIR:$PATH" \
    "$PACKAGE_DIR/setup.sh" \
    --secrets-dir "$SECRETS_DIR" \
    --secret-group "$(id -gn)" \
    > "$OUTPUT_FILE" 2>&1; then
    echo "Expected unhealthy RasHub setup to fail." >&2
    exit 1
fi

grep -Fq "health 'unhealthy'" "$OUTPUT_FILE"
grep -Fq 'RasHub setup failed.' "$OUTPUT_FILE"
grep -Fq 'Fix the reported problem and run ./setup.sh again.' "$OUTPUT_FILE"
! grep -Fq '  Password:' "$OUTPUT_FILE"
test "$environment_checksum" = "$(sha256sum "$PACKAGE_DIR/.env")"
test "$secret_checksum" = "$(sha256sum "$SECRETS_DIR/bootstrap-admin-password")"

mkdir -p "$FAILURE_PACKAGE_DIR"
cp "$ROOT_DIR/deploy/setup.sh" "$FAILURE_PACKAGE_DIR/setup.sh"
cp "$ROOT_DIR/deploy/compose.production.yaml" "$FAILURE_PACKAGE_DIR/compose.yaml"
cp "$ROOT_DIR/deploy/environments/.env.production.example" \
    "$FAILURE_PACKAGE_DIR/.env.example"
chmod 0755 "$FAILURE_PACKAGE_DIR/setup.sh"

if FAKE_DOCKER_LOG="$FAILURE_DOCKER_LOG" \
   FAKE_INSTALL_FAIL_DESTINATION="$FAILURE_PACKAGE_DIR/.env" \
   REAL_INSTALL="$REAL_INSTALL" \
   PATH="$FAKE_BIN_DIR:$PATH" \
    "$FAILURE_PACKAGE_DIR/setup.sh" \
    --secrets-dir "$FAILURE_SECRETS_DIR" \
    --secret-group "$(id -gn)" \
    > "$FAILURE_OUTPUT_FILE" 2>&1; then
    echo "Expected environment installation to fail." >&2
    exit 1
fi

grep -Fq 'Rolling back the incomplete first installation.' \
    "$FAILURE_OUTPUT_FILE"
grep -Fq 'Incomplete installation files were removed safely.' \
    "$FAILURE_OUTPUT_FILE"
test ! -e "$FAILURE_PACKAGE_DIR/.env"
test ! -e "$FAILURE_SECRETS_DIR/bootstrap-admin-password"
test ! -e "$FAILURE_SECRETS_DIR/data-protection-password"
test ! -e "$FAILURE_SECRETS_DIR/data-protection.pfx"

FAKE_DOCKER_LOG="$FAILURE_DOCKER_LOG" \
REAL_INSTALL="$REAL_INSTALL" \
PATH="$FAKE_BIN_DIR:$PATH" \
    "$FAILURE_PACKAGE_DIR/setup.sh" \
    --secrets-dir "$FAILURE_SECRETS_DIR" \
    --secret-group "$(id -gn)" \
    > "$FAILURE_OUTPUT_FILE"

test -f "$FAILURE_PACKAGE_DIR/.env"
test -f "$FAILURE_SECRETS_DIR/bootstrap-admin-password"
grep -Fq 'RasHub is running and healthy.' "$FAILURE_OUTPUT_FILE"

mkdir -p "$SOURCE_PACKAGE_DIR/environments"
cp "$ROOT_DIR/deploy/setup.sh" "$SOURCE_PACKAGE_DIR/setup.sh"
cp "$ROOT_DIR/deploy/compose.production.yaml" \
    "$SOURCE_PACKAGE_DIR/compose.production.yaml"
cp "$ROOT_DIR/deploy/compose.production.build.yaml" \
    "$SOURCE_PACKAGE_DIR/compose.production.build.yaml"
cp "$ROOT_DIR/deploy/environments/.env.production.example" \
    "$SOURCE_PACKAGE_DIR/environments/.env.production.example"
chmod 0755 "$SOURCE_PACKAGE_DIR/setup.sh"

FAKE_DOCKER_LOG="$SOURCE_DOCKER_LOG" \
PATH="$FAKE_BIN_DIR:$PATH" \
    "$SOURCE_PACKAGE_DIR/setup.sh" \
    --secrets-dir "$SOURCE_SECRETS_DIR" \
    --secret-group "$(id -gn)" \
    > "$SOURCE_OUTPUT_FILE"

test -f "$SOURCE_PACKAGE_DIR/environments/.env.production"
test -f "$SOURCE_SECRETS_DIR/bootstrap-admin-password"
grep -Fq ' pull postgres seq' "$SOURCE_DOCKER_LOG"
grep -Fq ' build migrate rashub' "$SOURCE_DOCKER_LOG"
grep -Fq 'RasHub is running and healthy.' "$SOURCE_OUTPUT_FILE"

echo "Release setup test passed."
