#!/bin/sh
set -eu
fail() { printf 'Serval development Agent startup failed: %s\n' "$1" >&2; exit 1; }
[ "$(id -u)" -eq 0 ] || fail 'startup hook must run as root'
[ -x /opt/serval/agent/Serval.Agent ] || fail 'Agent executable missing'
# Compose post_start can run before PID 1 has opened the systemd bus.
# Probe the live manager, rather than treating container start as readiness.
attempt=0
until timeout 2s systemctl show --property=SystemState --value >/dev/null 2>&1; do
    attempt=$((attempt + 1))
    [ "$attempt" -lt 10 ] || fail 'systemd manager unavailable; check PID 1 and private writable cgroup support'
    sleep 1
done
# /run is root-owned tmpfs. Reject a substituted path before systemd manages it.
[ ! -L /run/serval ] || fail 'unsafe runtime directory'
if [ -e /run/serval ]; then
    [ -d /run/serval ] || fail 'runtime path is not a directory'
    [ "$(stat -c %u:%g:%a /run/serval)" = "0:$(id -g serval-web):750" ] || fail 'unsafe runtime directory ownership or mode'
fi
[ ! -L /run/serval/agent.sock ] || fail 'unsafe socket path'
timeout 10s systemctl start serval-agent.service || fail 'systemd could not start Agent'
elapsed=0
while [ "$elapsed" -lt 15 ]; do
    timeout 2s systemctl is-active --quiet serval-agent.service || fail 'Agent unit is not active'
    if [ -S /run/serval/agent.sock ]; then
        /opt/serval/development/initialize-demos.sh
        printf 'Serval development Agent ready.\n'
        exit 0
    fi
    sleep 1
    elapsed=$((elapsed + 1))
done
fail 'Agent socket readiness timed out'
