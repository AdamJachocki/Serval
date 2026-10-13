#!/bin/sh

set -eu

fail() { printf 'Development host check failed: %s\n' "$1" >&2; exit 1; }

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

[ "$agent_count" -eq 1 ] || fail agent-process-count
[ "$web_count" -eq 1 ] || fail web-process-count
[ "$(awk '/^Uid:/ {print $2}' "/proc/$agent_pid/status")" -eq 0 ] || fail agent-process-uid
[ "$(awk '/^Uid:/ {print $2}' "/proc/$web_pid/status")" -eq "$(id -u serval-web)" ] || fail web-process-uid
[ -S /run/serval/agent.sock ] || fail agent-socket

if [ "${1:-}" != --health ]; then
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
