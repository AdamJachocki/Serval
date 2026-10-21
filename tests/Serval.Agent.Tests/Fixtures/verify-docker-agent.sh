#!/usr/bin/env bash
set -euo pipefail

fail() { echo "Agent fixture check failed: $1" >&2; exit 1; }

case "${1:-}" in
    identity)
        [[ $(id -u serval-web) -gt 0 ]] || fail web-uid
        [[ $(id -g serval-web) -gt 0 ]] || fail web-gid
        [[ $(id -G serval-web) == "$(id -g serval-web)" ]] || fail web-groups
        [[ $(getent passwd serval-web | cut -d: -f6-7) == /nonexistent:/usr/sbin/nologin ]] || fail web-account
        [[ $(getent shadow serval-web | cut -d: -f2) == '!'* ]] || fail web-password
        [[ $(getent group serval-admin | cut -d: -f3) -gt 0 ]] || fail admin-group
        [[ $(stat -c '%u:%g:%a' /etc/serval/agent.json) == 0:0:600 ]] || fail config-mode
        [[ $(stat -c '%u:%g:%a' /etc/pam.d/serval) == 0:0:644 ]] || fail pam-mode
        [[ $(stat -c '%u:%g:%a' /var/lib/serval) == 0:$(id -g serval-web):700 ]] || fail policy-dir
        [[ $(stat -c '%u:%g:%a' /opt/serval/agent/Serval.Agent) == 0:0:755 ]] || fail executable
        [[ $(id -u serval-allowed) -gt 0 && $(id -u serval-expired) -gt 0 ]] || fail test-accounts
        [[ $(id -G serval-allowed) == "$(id -g serval-allowed)" ]] || fail allowed-groups
        [[ $(id -G serval-expired) == "$(id -g serval-expired)" ]] || fail expired-groups
        ;;
    runtime)
        [[ $(cat /proc/1/comm) == systemd ]] || fail pid-one
        for path in /run /run/lock; do
            kind=$(awk -v mount="$path" '$5 == mount {for (i = 1; i <= NF; i++) if ($i == "-") {print $(i+1); exit}}' /proc/self/mountinfo)
            [[ $kind == tmpfs ]] || fail tmpfs-mount
        done
        [[ $(systemctl is-active serval-agent.service) == active ]] || fail service
        pid=$(systemctl show -p MainPID --value serval-agent.service)
        [[ $pid =~ ^[1-9][0-9]*$ && $(awk '/^Uid:/ {print $2}' "/proc/$pid/status") == 0 ]] || fail agent-uid
        [[ $(stat -c '%u:%g:%a' /run/serval) == 0:$(id -g serval-web):750 ]] || fail socket-dir
        [[ -S /run/serval/agent.sock ]] || fail socket-missing
        [[ $(stat -c '%u:%g:%a' /run/serval/agent.sock) == 0:$(id -g serval-web):660 ]] || fail socket-mode
        ;;
    startup-failures)
        good=$(mktemp -d)
        cp -p /etc/serval/agent.json "$good/agent.json"
        cp -p /etc/pam.d/serval "$good/serval.pam"
        restore() {
            systemctl stop serval-agent.service >/dev/null 2>&1 || true
            cp -p "$good/agent.json" /etc/serval/agent.json
            cp -p "$good/serval.pam" /etc/pam.d/serval
            systemctl reset-failed serval-agent.service >/dev/null 2>&1 || true
            rm -rf -- "$good"
        }
        trap restore EXIT
        check_failure() {
            systemctl reset-failed serval-agent.service >/dev/null 2>&1 || true
            systemctl start serval-agent.service >/dev/null 2>&1 || true
            sleep 3
            [[ $(systemctl is-active serval-agent.service 2>/dev/null || true) != active ]] || fail "$1-active"
            [[ ! -S /run/serval/agent.sock ]] || fail "$1-socket"
            systemctl stop serval-agent.service >/dev/null 2>&1 || true
        }
        systemctl stop serval-agent.service
        sed -i -E 's/"webUid":[0-9]+/"webUid":999999/' /etc/serval/agent.json
        check_failure wrong-web-uid
        cp -p "$good/agent.json" /etc/serval/agent.json
        chmod 0666 /etc/serval/agent.json
        check_failure unsafe-config-mode
        cp -p "$good/agent.json" /etc/serval/agent.json
        rm /etc/pam.d/serval
        check_failure missing-pam
        restore
        trap - EXIT
        systemctl start serval-agent.service
        sleep 2
        "$0" runtime
        ;;
    *) fail unknown-check ;;
esac
