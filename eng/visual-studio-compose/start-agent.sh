#!/bin/sh

set -eu

agent=/opt/serval/agent/Serval.Agent
socket=/run/serval/agent.sock
log=/tmp/serval-agent.log
timeout_seconds="${SERVAL_AGENT_START_TIMEOUT_SECONDS:-15}"

fail() {
    printf 'Serval development Agent startup failed: %s\n' "$1" >&2
    exit 1
}

[ "$(id -u)" -eq 0 ] || fail 'the Agent startup hook must run as root'
[ -x "$agent" ] || fail 'Serval.Agent is missing or not executable'

case "$timeout_seconds" in
    ''|*[!0-9]*) fail 'SERVAL_AGENT_START_TIMEOUT_SECONDS must be a positive integer' ;;
    0) fail 'SERVAL_AGENT_START_TIMEOUT_SECONDS must be greater than zero' ;;
esac

if pgrep -f "^${agent}$" >/dev/null 2>&1; then
    fail 'Serval.Agent is already running'
fi
if [ -e "$socket" ]; then
    fail 'the Agent socket already exists before startup'
fi

: > "$log"
chmod 0644 "$log"
setsid "$agent" >>"$log" 2>&1 </dev/null &
agent_pid=$!
elapsed=0
while [ ! -S "$socket" ]; do
    if ! kill -0 "$agent_pid" 2>/dev/null; then
        wait "$agent_pid" || true
        fail 'Serval.Agent exited before creating its socket'
    fi
    if [ "$elapsed" -ge "$timeout_seconds" ]; then
        kill -TERM "$agent_pid" 2>/dev/null || true
        wait "$agent_pid" 2>/dev/null || true
        fail "Serval.Agent did not create $socket within ${timeout_seconds}s"
    fi
    sleep 1
    elapsed=$((elapsed + 1))
done

printf 'Serval development Agent ready: PID %s, UID 0, socket %s\n' \
    "$agent_pid" "$socket"
