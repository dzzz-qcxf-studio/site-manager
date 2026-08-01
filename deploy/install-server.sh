#!/usr/bin/env bash
set -euo pipefail

readonly SERVICE_USER="sitepublisher"
readonly SERVICE_GROUP="sitepublisher"
readonly SERVICE_ROOT="/srv/site-manager"
readonly APPLICATION_ROOT="/opt/site-manager"
readonly NGINX_SNIPPET="/etc/nginx/snippets/site-manager-location.conf"
readonly PUBLIC_BASE_URL="http://47.86.89.203/s/"

DRY_RUN=true
SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
AUTHORIZED_KEY_FILE=""

usage() {
    cat <<'EOF'
Usage: sudo ./deploy/install-server.sh [--dry-run|--apply] [--source-dir <repository>] [--authorized-key-file <public-key-file>]

Without --apply the script only prints the actions it would take. It installs an
isolated Site Manager runtime and Nginx snippet; it never replaces an existing
web root or automatically injects the snippet into a server block.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --dry-run)
            DRY_RUN=true
            ;;
        --apply)
            DRY_RUN=false
            ;;
        --source-dir)
            SOURCE_DIR="$2"
            shift
            ;;
        --authorized-key-file)
            AUTHORIZED_KEY_FILE="$2"
            shift
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
    shift
done

require_command() {
    command -v "$1" >/dev/null 2>&1 || {
        echo "Required command is missing: $1" >&2
        exit 1
    }
}

run() {
    if "$DRY_RUN"; then
        printf '[dry-run]'
        printf ' %q' "$@"
        printf '\n'
        return
    fi
    "$@"
}

install_file() {
    local source="$1"
    local destination="$2"
    local mode="$3"
    run install -D -m "$mode" "$source" "$destination"
}

ensure_directory() {
    local directory="$1"
    local mode="$2"
    run install -d -o "$SERVICE_USER" -g "$SERVICE_GROUP" -m "$mode" "$directory"
}

if [[ "${EUID}" -ne 0 && "$DRY_RUN" == false ]]; then
    echo "--apply must run as root." >&2
    exit 1
fi

require_command python3
require_command nginx
require_command systemctl

if [[ ! -d "$SOURCE_DIR/server/site_manager" ]]; then
    echo "Could not find server/site_manager under: $SOURCE_DIR" >&2
    exit 1
fi

if [[ -n "$AUTHORIZED_KEY_FILE" && ! -f "$AUTHORIZED_KEY_FILE" ]]; then
    echo "Public key file does not exist: $AUTHORIZED_KEY_FILE" >&2
    exit 1
fi

echo "Mode: $([[ "$DRY_RUN" == true ]] && echo dry-run || echo apply)"
echo "Source: $SOURCE_DIR"
echo "Target content: $SERVICE_ROOT"

if ! id "$SERVICE_USER" >/dev/null 2>&1; then
    run useradd --create-home --shell /bin/bash "$SERVICE_USER"
fi

for directory in \
    "$SERVICE_ROOT" \
    "$SERVICE_ROOT/live" \
    "$SERVICE_ROOT/versions" \
    "$SERVICE_ROOT/staging" \
    "$SERVICE_ROOT/trash" \
    "$SERVICE_ROOT/registry/sites" \
    "$SERVICE_ROOT/locks"; do
    ensure_directory "$directory" 0750
done

run install -d -o root -g root -m 0755 "$APPLICATION_ROOT"
run install -d -o root -g root -m 0755 "$APPLICATION_ROOT/site_manager"
if "$DRY_RUN"; then
    echo "[dry-run] copy $SOURCE_DIR/server/site_manager/. to $APPLICATION_ROOT/site_manager"
else
    cp -a "$SOURCE_DIR/server/site_manager/." "$APPLICATION_ROOT/site_manager"
    chown -R root:root "$APPLICATION_ROOT/site_manager"
    find "$APPLICATION_ROOT/site_manager" -type d -exec chmod 0755 {} +
    find "$APPLICATION_ROOT/site_manager" -type f -exec chmod 0644 {} +
fi

if "$DRY_RUN"; then
    echo "[dry-run] write /usr/local/bin/site-managerctl"
else
    wrapper_path="$(mktemp)"
    trap 'rm -f -- "$wrapper_path"' EXIT
    cat >"$wrapper_path" <<EOF
#!/usr/bin/env bash
set -euo pipefail
export PYTHONPATH="$APPLICATION_ROOT"
export SITE_MANAGER_PUBLIC_BASE_URL="$PUBLIC_BASE_URL"
exec /usr/bin/python3 -m site_manager.cli "\$@"
EOF
    install -m 0755 "$wrapper_path" /usr/local/bin/site-managerctl
fi

if [[ -n "$AUTHORIZED_KEY_FILE" ]]; then
    if "$DRY_RUN"; then
        echo "[dry-run] install public key from $AUTHORIZED_KEY_FILE into /home/$SERVICE_USER/.ssh/authorized_keys"
    else
        user_home="$(getent passwd "$SERVICE_USER" | cut -d: -f6)"
        install -d -o "$SERVICE_USER" -g "$SERVICE_GROUP" -m 0700 "$user_home/.ssh"
        touch "$user_home/.ssh/authorized_keys"
        chown "$SERVICE_USER:$SERVICE_GROUP" "$user_home/.ssh/authorized_keys"
        chmod 0600 "$user_home/.ssh/authorized_keys"
        if ! grep -Fxq -- "$(<"$AUTHORIZED_KEY_FILE")" "$user_home/.ssh/authorized_keys"; then
            cat "$AUTHORIZED_KEY_FILE" >>"$user_home/.ssh/authorized_keys"
        fi
    fi
fi

install_file "$SOURCE_DIR/deploy/nginx/site-manager-location.conf" "$NGINX_SNIPPET" 0644
install_file "$SOURCE_DIR/deploy/nginx/site-manager-nginx.conf" /etc/nginx/site-manager-nginx.conf 0644
install_file "$SOURCE_DIR/deploy/nginx/site-manager-http-server.conf" /etc/nginx/site-manager-http.d/site-manager.conf 0644
install_file "$SOURCE_DIR/deploy/systemd/site-manager-purge.service" /etc/systemd/system/site-manager-purge.service 0644
install_file "$SOURCE_DIR/deploy/systemd/site-manager-purge.timer" /etc/systemd/system/site-manager-purge.timer 0644
install_file "$SOURCE_DIR/deploy/systemd/site-manager-web.service" /etc/systemd/system/site-manager-web.service 0644

run systemctl daemon-reload
run systemctl enable --now site-manager-purge.timer
run nginx -t
run nginx -t -c /etc/nginx/site-manager-nginx.conf

if "$DRY_RUN"; then
    echo "[dry-run] no Nginx server block has been modified or reloaded"
else
    echo "Installed the isolated runtime and validated the active Nginx configuration."
fi

cat <<EOF

Next Nginx step for an existing public HTTP server (after inspecting nginx -T and backing up the selected server block):
  include $NGINX_SNIPPET;

Place that one line inside the existing public server { ... } block, then run:
  nginx -t && systemctl reload nginx

Alternative for a host whose port 80 is verified unused: inspect and then start
the isolated Site Manager server (it serves only /s/ and returns 404 elsewhere):
  systemctl enable --now site-manager-web.service
EOF
