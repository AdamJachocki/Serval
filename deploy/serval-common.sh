#!/bin/sh
# Shared Serval deployment rules. Sourced (never executed) by the Debian maintainer scripts,
# serval-setup and deploy/install-identity.sh, so each rule exists once.
#
# POSIX sh, no side effects when sourced. Functions terminate the calling script with exit
# status 2 on a violated rule; call them from the main shell, not from a command substitution.
# Results are returned in SERVAL_* variables.

LC_ALL=C
export LC_ALL

serval_die() {
    printf '%s\n' "$1" >&2
    exit 2
}

# Fails when any argument is a symbolic link. Missing paths are fine.
serval_reject_symlinks() {
    for serval_path in "$@"; do
        if [ -L "$serval_path" ]; then
            serval_die 'Serval deployment path must not be a symbolic link.'
        fi
    done
}

# Validates the dedicated Web account and sets SERVAL_WEB_UID and SERVAL_WEB_GID.
serval_validate_web_identity() {
    serval_web_entry=$(getent passwd serval-web) ||
        serval_die 'Dedicated Serval Web identity is unavailable.'
    IFS=: read -r serval_web_name _ serval_entry_uid serval_entry_gid _ serval_web_home serval_web_shell << EOF
$serval_web_entry
EOF
    serval_shadow_entry=$(getent shadow serval-web) ||
        serval_die 'Dedicated Serval Web identity must have a locked local password.'
    IFS=: read -r serval_shadow_name serval_shadow_password _ << EOF
$serval_shadow_entry
EOF

    if [ "$serval_web_name" != serval-web ] || [ "$serval_web_home" != /nonexistent ] ||
        [ "$serval_web_shell" != /usr/sbin/nologin ] || [ "$serval_shadow_name" != serval-web ]; then
        serval_die 'Dedicated Serval Web identity must be locked and noninteractive.'
    fi
    case $serval_shadow_password in
        '!'* | '*'*) ;;
        *) serval_die 'Dedicated Serval Web identity must be locked and noninteractive.' ;;
    esac

    serval_web_uid=$(id -u serval-web)
    serval_web_gid=$(id -g serval-web)
    case $serval_web_uid$serval_web_gid in
        '' | *[!0-9]*) serval_die 'Dedicated Serval Web identity is invalid.' ;;
    esac
    if [ "$serval_web_uid" = 0 ] || [ "$serval_web_gid" = 0 ] ||
        [ "$serval_entry_uid" != "$serval_web_uid" ] || [ "$serval_entry_gid" != "$serval_web_gid" ] ||
        [ "$(id -gn serval-web)" != serval-web ]; then
        serval_die 'Dedicated Serval Web identity is invalid.'
    fi
    if [ "$(id -G serval-web)" != "$serval_web_gid" ]; then
        serval_die 'Dedicated Serval Web identity must have no supplementary groups.'
    fi

    # Results for the sourcing script.
    # shellcheck disable=SC2034
    SERVAL_WEB_UID=$serval_web_uid
    # shellcheck disable=SC2034
    SERVAL_WEB_GID=$serval_web_gid
}

# Validates a host-admin group name (grammar, existence, non-zero GID); sets SERVAL_ADMIN_GROUP
# and SERVAL_ADMIN_GID. The grammar is the one Serval.Agent enforces when it parses agent.json.
serval_validate_admin_group() {
    serval_group=${1-}
    case $serval_group in
        '' | [!a-z]* | *[!a-zA-Z0-9_-]*) serval_die 'Invalid host-admin group name.' ;;
    esac
    if [ "${#serval_group}" -gt 64 ]; then
        serval_die 'Invalid host-admin group name.'
    fi
    serval_group_entry=$(getent group "$serval_group") ||
        serval_die 'Host-admin group does not exist.'
    IFS=: read -r serval_group_name _ serval_group_gid _ << EOF
$serval_group_entry
EOF
    case $serval_group_gid in
        '' | 0* | *[!0-9]*) serval_die 'Host-admin group has an invalid GID.' ;;
    esac
    if [ "$serval_group_name" != "$serval_group" ]; then
        serval_die 'Host-admin group does not exist.'
    fi

    SERVAL_ADMIN_GROUP=$serval_group
    SERVAL_ADMIN_GID=$serval_group_gid
}

# serval_atomic_write DESTINATION MODE OWNER:GROUP
# Writes standard input to a temporary file in the destination directory, sets owner and mode,
# then renames it over the destination. Refuses symbolic-link destinations and empty content.
serval_atomic_write() {
    serval_dest=$1
    serval_mode=$2
    serval_owner=$3
    serval_dir=$(dirname -- "$serval_dest")
    serval_reject_symlinks "$serval_dir" "$serval_dest"
    [ -d "$serval_dir" ] || serval_die 'Serval destination directory is missing.'
    serval_tmp=$(mktemp "$serval_dir/.serval-write.XXXXXX") ||
        serval_die 'Could not create a temporary file.'
    if cat > "$serval_tmp" && [ -s "$serval_tmp" ] &&
        chown "$serval_owner" "$serval_tmp" && chmod "$serval_mode" "$serval_tmp" &&
        mv -f -- "$serval_tmp" "$serval_dest"; then
        return 0
    fi
    rm -f -- "$serval_tmp"
    serval_die 'Could not write a Serval file.'
}

# serval_write_agent_config PATH WEB_UID WEB_GID GROUP GID
# Creates agent.json (root, 0600) with the default idle interval. All values are validated
# first, so nothing attacker-controlled reaches the generated JSON.
serval_write_agent_config() {
    serval_cfg=$1
    case $2$3$5 in
        '' | *[!0-9]*) serval_die 'Invalid Agent configuration values.' ;;
    esac
    serval_validate_admin_group "$4"
    if [ "$SERVAL_ADMIN_GID" != "$5" ]; then
        serval_die 'Invalid Agent configuration values.'
    fi
    printf '{"webUid":%s,"webGid":%s,"adminGroup":"%s","adminGid":%s,"idleMinutes":15}\n' \
        "$2" "$3" "$SERVAL_ADMIN_GROUP" "$SERVAL_ADMIN_GID" |
        serval_atomic_write "$serval_cfg" 0600 root:root
}

# serval_rewrite_agent_group PATH GROUP
# Validates GROUP, then replaces only adminGroup and adminGid in an existing agent.json, so
# every other field (for example idleMinutes) survives. Nothing changes on any failure.
serval_rewrite_agent_group() {
    serval_cfg=$1
    command -v jq > /dev/null 2>&1 || serval_die 'jq is required to change the host-admin group.'
    serval_reject_symlinks "$serval_cfg"
    [ -f "$serval_cfg" ] || serval_die 'Agent configuration is missing.'
    serval_validate_admin_group "$2"
    serval_current=$(jq -e -c 'select(type == "object")' "$serval_cfg") ||
        serval_die 'Agent configuration is not valid JSON.'
    serval_next=$(printf '%s' "$serval_current" |
        jq -c --arg group "$SERVAL_ADMIN_GROUP" --argjson gid "$SERVAL_ADMIN_GID" \
            '.adminGroup = $group | .adminGid = $gid') ||
        serval_die 'Could not update the Agent configuration.'
    printf '%s\n' "$serval_next" | serval_atomic_write "$serval_cfg" 0600 root:root
}

# --- service lifecycle -------------------------------------------------------------------------

# True when systemd is PID 1; chroots and image builds skip service handling.
serval_have_systemd() {
    [ -d /run/systemd/system ]
}

# Stops one unit; a unit that is not loaded (systemctl status 5) counts as stopped.
serval_stop_unit() {
    if systemctl stop "$1"; then
        return 0
    else
        serval_rc=$?
    fi
    [ "$serval_rc" = 5 ] || serval_die "Could not stop $1."
}

# Stops Web first, then Agent (Web requires Agent).
serval_stop_services() {
    serval_have_systemd || return 0
    serval_stop_unit serval-web.service
    serval_stop_unit serval-agent.service
}

# Waits until the Agent is active and its socket exists. Agent is Type=simple, so a successful
# `systemctl start` does not prove that its startup validation passed.
serval_wait_for_agent() {
    serval_tries=0
    while [ "$serval_tries" -lt 30 ]; do
        if systemctl is-active --quiet serval-agent.service && [ -S /run/serval/agent.sock ]; then
            return 0
        fi
        serval_tries=$((serval_tries + 1))
        sleep 1
    done
    return 1
}

# Port from /etc/serval/web.json, or the loopback default.
serval_web_port() {
    if [ -f /etc/serval/web.json ] && [ ! -L /etc/serval/web.json ]; then
        jq -r '.port // 9080' /etc/serval/web.json 2> /dev/null || echo 9080
    else
        echo 9080
    fi
}

# True when the Web unit's own main process holds a listening socket on port $1.
serval_web_listens() {
    serval_wl_pid=$(systemctl show -p MainPID --value serval-web.service 2> /dev/null || echo 0)
    [ -n "$serval_wl_pid" ] && [ "$serval_wl_pid" != 0 ] || return 1
    serval_wl_hex=$(printf '%04X' "$1")
    serval_wl_inodes=$(awk -v port="$serval_wl_hex" '$4 == "0A" { split($2, a, ":"); if (a[2] == port) print $10 }' \
        /proc/net/tcp /proc/net/tcp6 2> /dev/null)
    [ -n "$serval_wl_inodes" ] || return 1
    for serval_wl_inode in $serval_wl_inodes; do
        for serval_wl_fd in /proc/"$serval_wl_pid"/fd/*; do
            if [ "$(readlink "$serval_wl_fd" 2> /dev/null)" = "socket:[$serval_wl_inode]" ]; then
                return 0
            fi
        done
    done
    return 1
}

# Waits until Web is active and listening on port $1 for two checks one second apart (so a service
# that binds and then crashes is not reported as healthy).
serval_wait_for_web() {
    serval_ww_tries=0
    while [ "$serval_ww_tries" -lt 20 ]; do
        if systemctl is-active --quiet serval-web.service && serval_web_listens "$1"; then
            sleep 1
            if systemctl is-active --quiet serval-web.service && serval_web_listens "$1"; then
                return 0
            fi
        fi
        serval_ww_tries=$((serval_ww_tries + 1))
        sleep 1
    done
    return 1
}

# Starts Agent, verifies it, then starts Web and verifies that it listens. Web is never started
# after an Agent failure.
serval_start_services() {
    serval_have_systemd || return 0
    systemctl start serval-agent.service || serval_die 'Could not start serval-agent.service.'
    serval_wait_for_agent ||
        serval_die 'serval-agent.service did not become ready; serval-web.service was not started. See: journalctl -u serval-agent.service'
    systemctl reset-failed serval-web.service > /dev/null 2>&1 || true
    systemctl start serval-web.service || serval_die 'Could not start serval-web.service.'
    serval_wait_for_web "$(serval_web_port)" ||
        serval_die 'serval-web.service did not start listening. See: journalctl -u serval-web.service'
}

# Restarts only Web (listener changes); clears a start-limit state left by an earlier failure first.
serval_restart_web() {
    serval_have_systemd || return 0
    systemctl reset-failed serval-web.service > /dev/null 2>&1 || true
    systemctl restart serval-web.service > /dev/null 2>&1
}

# Restarts both units in order (Agent first, Web only after a verified Agent).
serval_restart_services() {
    serval_stop_services
    serval_start_services
}

# serval_apply_admin_group GROUP
# The one path used by `dpkg-reconfigure serval` and `serval-setup admin-group`: validate, rewrite
# agent.json atomically, restart Agent then Web. If the services do not come back, the previous
# agent.json is restored, the services are restarted again and the script exits non-zero.
serval_apply_admin_group() {
    serval_validate_admin_group "$1"
    serval_cfg=/etc/serval/agent.json
    serval_reject_symlinks "$serval_cfg"
    [ -f "$serval_cfg" ] || serval_die 'Agent configuration is missing.'
    serval_saved=$(mktemp /etc/serval/.serval-saved.XXXXXX) || serval_die 'Could not create a temporary file.'
    if ! cp -- "$serval_cfg" "$serval_saved" || ! chmod 0600 "$serval_saved"; then
        rm -f -- "$serval_saved"
        serval_die 'Could not save the Agent configuration.'
    fi
    if ! (serval_rewrite_agent_group "$serval_cfg" "$SERVAL_ADMIN_GROUP"); then
        rm -f -- "$serval_saved"
        exit 2
    fi
    if (serval_restart_services); then
        rm -f -- "$serval_saved"
        return 0
    fi
    # The helpers exit the shell they run in, so run them in subshells: the cleanup and the restart still happen.
    (serval_atomic_write "$serval_cfg" 0600 root:root < "$serval_saved") || true
    rm -f -- "$serval_saved"
    (serval_restart_services) || true
    serval_die 'The services did not start with the new host-admin group; the previous configuration was restored.'
}
