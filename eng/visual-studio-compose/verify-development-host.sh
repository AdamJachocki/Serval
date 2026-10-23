#!/bin/sh

set -eu

fail() { printf 'Development host check failed: %s\n' "$1" >&2; exit 1; }

mode=${1:-full}
case "$mode" in
    --health|full) ;;
    *) fail "unknown verification mode: $mode" ;;
esac

timeout 2s systemctl show --property=SystemState --value >/dev/null 2>&1 || fail systemd-manager
timeout 2s systemctl is-active --quiet serval-agent.service || fail Agent-unit
for pair in running:active inactive:inactive failed:failed; do
    name=${pair%:*}
    expected=${pair#*:}
    unit="serval-demo-$name.service"
    loaded=$(timeout 2s systemctl show "$unit" --property=LoadState --value) || fail "$unit: query"
    [ "$loaded" = loaded ] || fail "$unit: missing"
    actual=$(timeout 2s systemctl show "$unit" --property=ActiveState --value) || fail "$unit: query"
    [ "$actual" = "$expected" ] || fail "$unit: expected $expected, got $actual"
done

agent_count=0
agent_pid=
web_count=0
web_pid=

for process in /proc/[1-9]*; do
    [ -r "$process/status" ] || continue
    pid=${process##*/}
    first_argument=$(tr '\000' '\n' < "$process/cmdline" 2>/dev/null | sed -n '1p')
    if [ "$first_argument" = /opt/serval/agent/Serval.Agent ]; then
        agent_count=$((agent_count + 1))
        agent_pid=$pid
    fi
    if { [ "$first_argument" = dotnet ] || [ "$first_argument" = /usr/bin/dotnet ]; } && \
        tr '\000' '\n' < "$process/cmdline" 2>/dev/null | grep -Eq '(^|/)Serval\.Web\.dll$'; then
        web_count=$((web_count + 1))
        web_pid=$pid
    fi
done

[ "$agent_count" -eq 1 ] || fail "expected exactly one Serval.Agent process, found $agent_count"
[ "$(awk '/^Uid:/ {print $2}' "/proc/$agent_pid/status")" -eq 0 ] || fail 'Serval.Agent is not running as UID 0'
[ -S /run/serval/agent.sock ] || fail 'the Agent socket is missing at /run/serval/agent.sock'
[ ! -L /run/serval ] && [ ! -L /run/serval/agent.sock ] || fail unsafe-socket-path
[ "$(stat -c %u:%g:%a /run/serval)" = "0:$(id -g serval-web):750" ] || fail runtime-directory-permissions
[ "$(stat -c %u:%g:%a /run/serval/agent.sock)" = "0:$(id -g serval-web):660" ] || fail socket-permissions

if [ "$mode" = full ]; then
    [ "$web_count" -eq 1 ] || fail "expected exactly one Serval.Web process, found $web_count"
    [ "$(awk '/^Uid:/ {print $2}' "/proc/$web_pid/status")" -eq "$(id -u serval-web)" ] || \
        fail 'Serval.Web is not running as the serval-web kernel UID'
    if grep -E '^Cap(Inh|Prm|Eff|Bnd|Amb):' "/proc/$web_pid/status" | grep -qv '0000000000000000$'; then
        fail 'Serval.Web retained capabilities'
    fi

    login_page=$(mktemp)
    trap 'rm -f "$login_page"' EXIT
    curl --fail --silent --show-error --max-time 5 http://127.0.0.1:8080/Login >"$login_page" || fail web-request
    stylesheets=$(sed -n 's/.*<link[^>]*href="\([^"]*\.css\)"[^>]*>.*/\1/p' "$login_page")
    [ -n "$stylesheets" ] || fail static-web-asset-links
    for stylesheet in $stylesheets; do
        case "$stylesheet" in
            /*) ;;
            *) fail static-web-asset-path ;;
        esac
        curl --fail --silent --show-error --max-time 5 \
            "http://127.0.0.1:8080$stylesheet" >/dev/null || fail static-web-asset
    done
fi
