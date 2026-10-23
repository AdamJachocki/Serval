#!/bin/sh
set -eu
fail() { printf 'Development sample initialization failed: %s\n' "$1" >&2; exit 1; }
[ "$#" -eq 0 ] && [ "$(id -u)" -eq 0 ] || fail invocation
units='serval-demo-running.service serval-demo-inactive.service serval-demo-failed.service'
# This directory belongs to this image, is not mounted, and persists only for
# the lifetime of its disposable container. Never overwrite an existing unit.
state=/var/lib/serval-development
[ ! -L "$state" ] && [ -d "$state" ] || fail unsafe-state-directory
[ "$(stat -c %u:%g:%a "$state")" = 0:0:700 ] || fail unsafe-state-directory
[ ! -L "$state/initialized" ] || fail unsafe-marker
if [ -e "$state/initialized" ]; then
    [ -f "$state/initialized" ] && [ "$(stat -c %u "$state/initialized")" = 0 ] || fail unsafe-marker
    for unit in $units; do
        [ ! -L "/etc/systemd/system/$unit" ] || fail "$unit: unsafe destination"
        cmp -s "/opt/serval/development/$unit" "/etc/systemd/system/$unit" || fail "$unit: changed unit"
    done
    exit 0
fi
for unit in $units; do
    [ ! -e "/etc/systemd/system/$unit" ] && [ ! -L "/etc/systemd/system/$unit" ] || fail "$unit: destination exists"
    # Query the manager, including loaded units and aliases in every unit path.
    loaded=$(timeout 2s systemctl show "$unit" --property=LoadState --value) || fail manager-query
    [ "$loaded" = not-found ] || fail "$unit: manager name collision"
    status=0
    files=$(timeout 2s systemctl list-unit-files "$unit" --no-legend --no-pager) || status=$?
    # systemctl returns 1 for an empty matching unit-file set.
    [ "$status" -le 1 ] || fail manager-query
    [ -z "$files" ] || fail "$unit: installed name collision"
    timeout 2s systemctl show --property=SystemState --value >/dev/null || fail manager-query
done
for unit in $units; do
    install -o root -g root -m 0644 "/opt/serval/development/$unit" "/etc/systemd/system/$unit"
done
timeout 5s systemctl daemon-reload || fail daemon-reload
timeout 10s systemctl start serval-demo-running.service || fail running-start
if timeout 10s systemctl start serval-demo-failed.service >/dev/null 2>&1; then
    fail 'failed sample unexpectedly succeeded'
fi
for pair in running:active inactive:inactive failed:failed; do
    name=${pair%:*}
    expected=${pair#*:}
    actual=$(timeout 2s systemctl show "serval-demo-$name.service" --property=ActiveState --value) || fail manager-query
    [ "$actual" = "$expected" ] || fail "$name: unexpected state"
done
touch "$state/initialized"
