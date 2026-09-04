#!/usr/bin/env bash

if [ -z "${BASH_VERSION:-}" ] || [ -z "${BASH_SOURCE+x}" ]; then
    if command -v bash >/dev/null 2>&1; then
        exec bash "$0" "$@"
    fi

    echo "Error: setup.sh requires Bash." >&2
    exit 1
fi

set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SECRETS_DIR="/opt/rashub/secrets"
SECRET_GROUP="rashub-secrets"
ADMIN_EMAIL="rashub@rashub"
SEQ_ADMIN_USERNAME="admin"
SEQ_IMAGE="datalust/seq:2026.1"
SEQ_PUBLIC_URL="http://127.0.0.1:5341/"
READINESS_TIMEOUT_SECONDS=300

if [[ -f "$SCRIPT_DIR/.env.example" && -f "$SCRIPT_DIR/compose.yaml" ]]; then
    INSTALLATION_KIND="release"
    ENV_TEMPLATE="$SCRIPT_DIR/.env.example"
    ENV_FILE="$SCRIPT_DIR/.env"
    COMPOSE_FILE_ARGUMENTS=(--file "$SCRIPT_DIR/compose.yaml")
elif [[ -f "$SCRIPT_DIR/environments/.env.production.example" &&
        -f "$SCRIPT_DIR/compose.production.yaml" &&
        -f "$SCRIPT_DIR/compose.production.build.yaml" ]]; then
    INSTALLATION_KIND="source"
    ENV_TEMPLATE="$SCRIPT_DIR/environments/.env.production.example"
    ENV_FILE="$SCRIPT_DIR/environments/.env.production"
    COMPOSE_FILE_ARGUMENTS=(
        --file "$SCRIPT_DIR/compose.production.yaml"
        --file "$SCRIPT_DIR/compose.production.build.yaml"
    )
else
    echo "Error: setup.sh is not inside a RasHub deployment bundle or source checkout." >&2
    exit 1
fi

fresh_installation=0
credentials_printed=0
temporary_dir=""
admin_password=""
seq_password=""
first_installation_commit_started=0
environment_install_attempted=0
secret_install_uses_privilege=0
installed_secret_files=()

usage() {
    cat <<'EOF'
Usage: ./setup.sh [--secrets-dir ABSOLUTE_PATH] [--secret-group GROUP]

Install or start RasHub. On the first run the script creates all configuration
and secrets. Every run validates the stack, obtains the required images,
starts migrations and services, and waits until RasHub is healthy.
EOF
}

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "Error: required command '$1' was not found." >&2
        exit 1
    fi
}

run_privileged() {
    if (( EUID == 0 )); then
        "$@"
        return
    fi

    require_command sudo
    sudo "$@"
}

compose() {
    docker compose \
        --env-file "$ENV_FILE" \
        "${COMPOSE_FILE_ARGUMENTS[@]}" \
        "$@"
}

cleanup() {
    if [[ -n "$temporary_dir" && -d "$temporary_dir" ]]; then
        rm -rf -- "$temporary_dir"
    fi
}

rollback_incomplete_first_installation() {
    local installed_secret rollback_failed=0

    if (( !first_installation_commit_started )); then
        return 0
    fi

    echo "Rolling back the incomplete first installation..." >&2

    if (( environment_install_attempted )) && ! rm -f -- "$ENV_FILE"; then
        echo "Warning: unable to remove incomplete environment file: $ENV_FILE" >&2
        rollback_failed=1
    fi

    for installed_secret in "${installed_secret_files[@]}"; do
        if (( secret_install_uses_privilege )); then
            if ! run_privileged rm -f -- "$installed_secret"; then
                echo "Warning: unable to remove incomplete secret: $installed_secret" >&2
                rollback_failed=1
            fi
        elif ! rm -f -- "$installed_secret"; then
            echo "Warning: unable to remove incomplete secret: $installed_secret" >&2
            rollback_failed=1
        fi
    done

    if (( rollback_failed )); then
        echo "Warning: manual cleanup is required before setup can be retried." >&2
        return 1
    fi

    first_installation_commit_started=0
    echo "Incomplete installation files were removed safely." >&2
}

print_initial_credentials() {
    cat <<EOF

RasHub administrator
  Login:    $ADMIN_EMAIL
  Password: $admin_password

Seq administrator
  Login:    $SEQ_ADMIN_USERNAME
  Password: $seq_password

Store these credentials in a password manager now. They are printed only by
this initial setup run and are never sent to the RasHub or Seq logger.
EOF
    credentials_printed=1
}

handle_error() {
    local exit_code="$1"
    trap - ERR
    set +e

    echo >&2
    echo "RasHub setup failed." >&2

    if [[ -f "$ENV_FILE" ]] && command -v docker >/dev/null 2>&1; then
        compose ps --all >&2
        compose logs --no-color --tail 100 migrate rashub >&2
    fi

    rollback_incomplete_first_installation || true

    if (( fresh_installation && !credentials_printed )) &&
       [[ -n "$admin_password" && -n "$seq_password" ]]; then
        print_initial_credentials >&2
    fi

    echo "Fix the reported problem and run ./setup.sh again." >&2
    exit "$exit_code"
}

handle_signal() {
    local signal_name="$1"
    local exit_code="$2"
    trap - ERR HUP INT TERM
    set +e

    echo >&2
    echo "RasHub setup was interrupted by $signal_name." >&2
    rollback_incomplete_first_installation || true

    if (( fresh_installation && !credentials_printed )) &&
       [[ -n "$admin_password" && -n "$seq_password" ]]; then
        print_initial_credentials >&2
    fi

    exit "$exit_code"
}

random_base64() {
    local byte_count="$1"
    openssl rand -base64 "$byte_count" | tr -d '\r\n'
}

random_hex() {
    local byte_count="$1"
    openssl rand -hex "$byte_count" | tr -d '\r\n'
}

replace_environment_values() {
    local destination="$1"
    local line key
    local replaced_admin=0
    local replaced_gid=0
    local replaced_bootstrap_file=0
    local replaced_certificate=0
    local replaced_certificate_password=0
    local replaced_seq_url=0
    local replaced_seq_hash=0
    local replaced_postgres_password=0

    while IFS= read -r line || [[ -n "$line" ]]; do
        key="${line%%=*}"

        case "$key" in
            RASHUB_BOOTSTRAP_ADMIN_EMAIL)
                printf '%s=%s\n' "$key" "$ADMIN_EMAIL"
                replaced_admin=1
                ;;
            RASHUB_SECRET_GID)
                printf '%s=%s\n' "$key" "$secret_gid"
                replaced_gid=1
                ;;
            RASHUB_BOOTSTRAP_ADMIN_PASSWORD_FILE)
                printf '%s=%s/bootstrap-admin-password\n' "$key" "$SECRETS_DIR"
                replaced_bootstrap_file=1
                ;;
            RASHUB_DATA_PROTECTION_CERTIFICATE_PATH)
                printf '%s=%s/data-protection.pfx\n' "$key" "$SECRETS_DIR"
                replaced_certificate=1
                ;;
            RASHUB_DATA_PROTECTION_CERTIFICATE_PASSWORD_FILE)
                printf '%s=%s/data-protection-password\n' "$key" "$SECRETS_DIR"
                replaced_certificate_password=1
                ;;
            SEQ_PUBLIC_URL)
                printf '%s=%s\n' "$key" "$SEQ_PUBLIC_URL"
                replaced_seq_url=1
                ;;
            SEQ_ADMIN_PASSWORD_HASH)
                printf '%s=%s\n' "$key" "$seq_password_hash"
                replaced_seq_hash=1
                ;;
            POSTGRES_PASSWORD)
                printf '%s=%s\n' "$key" "$postgres_password"
                replaced_postgres_password=1
                ;;
            *)
                printf '%s\n' "$line"
                ;;
        esac
    done < "$ENV_TEMPLATE" > "$destination"

    if (( !replaced_admin || !replaced_gid || !replaced_bootstrap_file ||
          !replaced_certificate || !replaced_certificate_password ||
          !replaced_seq_url || !replaced_seq_hash ||
          !replaced_postgres_password )); then
        echo "Error: .env.example is missing one or more setup values." >&2
        return 1
    fi
}

read_environment_value() {
    local requested_key="$1"
    local line key

    while IFS= read -r line || [[ -n "$line" ]]; do
        key="${line%%=*}"

        if [[ "$key" == "$requested_key" ]]; then
            printf '%s\n' "${line#*=}"
            return 0
        fi
    done < "$ENV_FILE"

    return 1
}

prepare_first_installation() {
    local group_record secret_gid secrets_parent use_privileged_install
    local secret_name data_protection_password postgres_password
    local seq_password_hash

    require_command getent
    require_command install
    require_command mktemp
    require_command openssl
    require_command tr

    if ! getent group "$SECRET_GROUP" >/dev/null 2>&1; then
        echo "Creating system group '$SECRET_GROUP'..."

        if (( EUID == 0 )); then
            require_command groupadd
        fi

        run_privileged groupadd --system "$SECRET_GROUP"
    fi

    group_record="$(getent group "$SECRET_GROUP")"
    IFS=: read -r _ _ secret_gid _ <<< "$group_record"

    if [[ ! "$secret_gid" =~ ^[0-9]+$ ]]; then
        echo "Error: unable to resolve the numeric GID for '$SECRET_GROUP'." >&2
        return 1
    fi

    secrets_parent="$(dirname "$SECRETS_DIR")"
    use_privileged_install=1

    if [[ "$secret_gid" == "$(id -g)" ]]; then
        if [[ -d "$SECRETS_DIR" && -w "$SECRETS_DIR" ]] ||
           [[ ! -e "$SECRETS_DIR" && -d "$secrets_parent" && -w "$secrets_parent" ]]; then
            use_privileged_install=0
        fi
    fi

    for secret_name in \
        bootstrap-admin-password \
        data-protection-password \
        data-protection.pfx; do
        if (( use_privileged_install )); then
            if run_privileged test -e "$SECRETS_DIR/$secret_name"; then
                echo "Error: refusing to overwrite existing secret: $SECRETS_DIR/$secret_name" >&2
                return 1
            fi
        elif [[ -e "$SECRETS_DIR/$secret_name" ]]; then
            echo "Error: refusing to overwrite existing secret: $SECRETS_DIR/$secret_name" >&2
            return 1
        fi
    done

    temporary_dir="$(mktemp -d)"
    chmod 0700 "$temporary_dir"

    admin_password="$(random_base64 24)"
    postgres_password="$(random_hex 32)"
    seq_password="$(random_base64 24)"
    data_protection_password="$(random_base64 48)"

    printf '%s\n' "$admin_password" > "$temporary_dir/bootstrap-admin-password"
    printf '%s\n' "$data_protection_password" > "$temporary_dir/data-protection-password"

    openssl req \
        -x509 \
        -newkey rsa:4096 \
        -sha256 \
        -days 3650 \
        -nodes \
        -subj "/CN=RasHub Data Protection" \
        -keyout "$temporary_dir/data-protection.key" \
        -out "$temporary_dir/data-protection.crt" \
        >/dev/null 2>&1

    openssl pkcs12 \
        -export \
        -out "$temporary_dir/data-protection.pfx" \
        -inkey "$temporary_dir/data-protection.key" \
        -in "$temporary_dir/data-protection.crt" \
        -passout "file:$temporary_dir/data-protection-password" \
        >/dev/null 2>&1

    echo "Generating the Seq administrator password hash..."
    seq_password_hash="$(
        printf '%s' "$seq_password" |
            docker run --rm -i "$SEQ_IMAGE" config hash
    )"
    seq_password_hash="${seq_password_hash//$'\r'/}"
    seq_password_hash="${seq_password_hash//$'\n'/}"

    if [[ -z "$seq_password_hash" ]]; then
        echo "Error: Seq returned an empty administrator password hash." >&2
        return 1
    fi

    replace_environment_values "$temporary_dir/.env"
    chmod 0600 "$temporary_dir/.env"

    first_installation_commit_started=1
    secret_install_uses_privilege="$use_privileged_install"

    if (( use_privileged_install )); then
        run_privileged install -d -o root -g "$secret_gid" -m 0750 "$SECRETS_DIR"
    else
        install -d -g "$secret_gid" -m 0750 "$SECRETS_DIR"
    fi

    for secret_name in \
        bootstrap-admin-password \
        data-protection-password \
        data-protection.pfx; do
        installed_secret_files+=("$SECRETS_DIR/$secret_name")

        if (( use_privileged_install )); then
            run_privileged install -o root -g "$secret_gid" -m 0640 \
                "$temporary_dir/$secret_name" \
                "$SECRETS_DIR/$secret_name"
        else
            install -g "$secret_gid" -m 0640 \
                "$temporary_dir/$secret_name" \
                "$SECRETS_DIR/$secret_name"
        fi
    done

    # The environment file is the commit marker for a complete first
    # installation. Do not allow an interrupt between writing it and updating
    # the in-memory state used by the signal handler.
    trap '' HUP INT TERM
    environment_install_attempted=1
    install -m 0600 "$temporary_dir/.env" "$ENV_FILE"
    fresh_installation=1
    first_installation_commit_started=0
    trap 'handle_signal HUP 129' HUP
    trap 'handle_signal INT 130' INT
    trap 'handle_signal TERM 143' TERM
}

obtain_images() {
    if [[ "$INSTALLATION_KIND" == "release" ]]; then
        echo "Downloading the published RasHub stack..."
        compose pull
        return
    fi

    echo "Downloading infrastructure images..."
    compose pull postgres seq
    echo "Building RasHub from the current source checkout..."
    compose build migrate rashub
}

wait_for_rashub() {
    local deadline container_id status health
    deadline=$((SECONDS + READINESS_TIMEOUT_SECONDS))

    echo "Waiting for RasHub to become healthy..."

    while (( SECONDS < deadline )); do
        container_id="$(compose ps --all -q rashub 2>/dev/null || true)"

        if [[ -n "$container_id" ]]; then
            status="$(
                docker inspect --format '{{.State.Status}}' "$container_id" \
                    2>/dev/null || true
            )"
            health="$(
                docker inspect \
                    --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' \
                    "$container_id" \
                    2>/dev/null || true
            )"

            if [[ "$status" == "running" && "$health" == "healthy" ]]; then
                return 0
            fi

            if [[ "$status" == "exited" || "$status" == "dead" ||
                  "$health" == "unhealthy" ]]; then
                echo "Error: RasHub entered state '$status' with health '$health'." >&2
                return 1
            fi
        fi

        sleep 2
    done

    echo "Error: RasHub did not become healthy within $READINESS_TIMEOUT_SECONDS seconds." >&2
    return 1
}

trap cleanup EXIT
trap 'handle_error $?' ERR
trap 'handle_signal HUP 129' HUP
trap 'handle_signal INT 130' INT
trap 'handle_signal TERM 143' TERM

while (( $# > 0 )); do
    case "$1" in
        --secrets-dir)
            if (( $# < 2 )); then
                echo "Error: --secrets-dir requires a value." >&2
                exit 2
            fi

            SECRETS_DIR="$2"
            shift 2
            ;;
        --secret-group)
            if (( $# < 2 )); then
                echo "Error: --secret-group requires a value." >&2
                exit 2
            fi

            SECRET_GROUP="$2"
            shift 2
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            echo "Error: unknown argument '$1'." >&2
            usage >&2
            exit 2
            ;;
    esac
done

if [[ "$SECRETS_DIR" != /* || "$SECRETS_DIR" == "/" ]]; then
    echo "Error: --secrets-dir must be a specific absolute path." >&2
    exit 2
fi

require_command docker
docker compose version >/dev/null

if [[ ! -e "$ENV_FILE" ]]; then
    prepare_first_installation
else
    if [[ ! -f "$ENV_FILE" ]]; then
        echo "Error: expected an environment file at $ENV_FILE." >&2
        exit 1
    fi

    echo "Using the existing RasHub configuration. Credentials are unchanged."
fi

echo "Validating the deployment configuration..."
compose config --quiet
obtain_images

echo "Starting RasHub, PostgreSQL, Seq, and database migrations..."
compose up --detach --remove-orphans
wait_for_rashub

bind_address="$(read_environment_value RASHUB_BIND_ADDRESS || true)"
bind_port="$(read_environment_value RASHUB_PORT || true)"
bind_address="${bind_address:-127.0.0.1}"
bind_port="${bind_port:-8080}"

echo
echo "RasHub is running and healthy."
echo "  Local address: http://$bind_address:$bind_port"

if (( fresh_installation )); then
    print_initial_credentials
else
    echo "  Existing administrator credentials were not changed."
fi

echo
echo "Run ./setup.sh again whenever the existing stack needs to be validated and started."
