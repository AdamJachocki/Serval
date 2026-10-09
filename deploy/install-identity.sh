#!/usr/bin/env bash
set -euo pipefail

if (( EUID != 0 )) || (( $# != 1 )); then
    echo 'Usage (as root): bash deploy/install-identity.sh <existing-host-admin-group>' >&2
    exit 2
fi

admin_group=$1
if [[ ! $admin_group =~ ^[a-z][a-zA-Z0-9_-]{0,63}$ ]]; then
    echo 'Invalid host-admin group name.' >&2
    exit 2
fi

admin_entry=$(getent group "$admin_group") || {
    echo 'Host-admin group does not exist.' >&2
    exit 2
}
admin_gid=$(printf '%s' "$admin_entry" | cut -d: -f3)
if [[ ! $admin_gid =~ ^[1-9][0-9]*$ ]]; then
    echo 'Host-admin group has an invalid GID.' >&2
    exit 2
fi

if ! getent passwd serval-web >/dev/null; then
    useradd --system --user-group --no-create-home --home-dir /nonexistent \
        --shell /usr/sbin/nologin serval-web
fi

bash deploy/validate-web-identity.sh
web_uid=$(id -u serval-web)
web_gid=$(id -g serval-web)

for protected_path in /etc/serval /var/lib/serval /etc/pam.d/serval; do
    if [[ -L $protected_path ]]; then
        echo 'Serval deployment path must not be a symbolic link.' >&2
        exit 2
    fi
done

install -d -o root -g root -m 0755 /etc/serval
install -d -o root -g root -m 0700 /var/lib/serval
install -o root -g root -m 0644 deploy/pam.d/serval /etc/pam.d/serval
config=$(mktemp /etc/serval/agent.json.XXXXXX)
trap 'rm -f -- "$config"' EXIT
printf '{"webUid":%s,"webGid":%s,"adminGroup":"%s","adminGid":%s,"idleMinutes":15}\n' \
    "$web_uid" "$web_gid" "$admin_group" "$admin_gid" > "$config"
chown root:root "$config"
chmod 0600 "$config"
mv -f -- "$config" /etc/serval/agent.json
trap - EXIT

install -o root -g root -m 0644 deploy/systemd/serval-agent.service \
    /etc/systemd/system/serval-agent.service
install -o root -g root -m 0644 deploy/systemd/serval-web.service \
    /etc/systemd/system/serval-web.service
systemctl daemon-reload

echo 'Serval identities and unit files installed. Publish the Agent and Web binaries before starting services.'
