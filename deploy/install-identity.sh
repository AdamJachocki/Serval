#!/usr/bin/env bash
set -euo pipefail

if (( EUID != 0 )) || (( $# != 1 )); then
    echo 'Usage (as root): bash deploy/install-identity.sh <existing-host-admin-group>' >&2
    exit 2
fi

deploy_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=deploy/serval-common.sh
. "$deploy_dir/serval-common.sh"

serval_validate_admin_group "$1"

if ! getent passwd serval-web >/dev/null; then
    useradd --system --user-group --no-create-home --home-dir /nonexistent \
        --shell /usr/sbin/nologin serval-web
fi

serval_validate_web_identity
serval_reject_symlinks /etc/serval /var/lib/serval /etc/pam.d/serval

install -d -o root -g root -m 0755 /etc/serval
install -d -o root -g root -m 0700 /var/lib/serval
install -o root -g root -m 0644 "$deploy_dir/pam.d/serval" /etc/pam.d/serval
serval_write_agent_config /etc/serval/agent.json \
    "$SERVAL_WEB_UID" "$SERVAL_WEB_GID" "$SERVAL_ADMIN_GROUP" "$SERVAL_ADMIN_GID"

install -o root -g root -m 0644 "$deploy_dir/systemd/serval-agent.service" \
    /etc/systemd/system/serval-agent.service
install -o root -g root -m 0644 "$deploy_dir/systemd/serval-web.service" \
    /etc/systemd/system/serval-web.service
systemctl daemon-reload

echo 'Serval identities and unit files installed. Publish the Agent and Web binaries before starting services.'
