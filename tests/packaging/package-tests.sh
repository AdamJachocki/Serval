#!/usr/bin/env bash
# Package lifecycle tests. Runs as root INSIDE a systemd test container (see run-package-tests.sh):
#   NEW_DEB=/pkg/serval_0.2.0_amd64.deb OLD_DEB=/pkg/serval_0.1.0_amd64.deb bash package-tests.sh [scenario...]
# Every scenario starts from a clean host (reset) and asserts observable state, never log text alone.
# SC2015: "cmd && pass || fail" is intended, pass cannot fail. SC2016: snippets for bash -c are single-quoted on purpose.
# shellcheck disable=SC2015,SC2016
set -uo pipefail

NEW_DEB=${NEW_DEB:?NEW_DEB is required}
OLD_DEB=${OLD_DEB:?OLD_DEB is required}
REPO=${REPO:-/repo}
export DEBIAN_FRONTEND=noninteractive
LOG=$(mktemp)
failures=0
apt-get update -qq > /dev/null 2>&1

pass() { printf 'ok   %s\n' "$1"; }
fail() { printf 'FAIL %s\n' "$1" >&2; failures=$((failures + 1)); }
assert() { local description=$1; shift; if "$@" > /dev/null 2>&1; then pass "$description"; else fail "$description"; fi; }
refute() { local description=$1; shift; if "$@" > /dev/null 2>&1; then fail "$description"; else pass "$description"; fi; }
equals() { if [[ $2 == "$3" ]]; then pass "$1"; else fail "$1 (expected '$3', got '$2')"; fi; }

apt_install() { apt-get install -y -qq --allow-downgrades "$@" > "$LOG" 2>&1; }
active() { [[ $(systemctl is-active "$1" 2> /dev/null) == active ]]; }
sql() { sqlite3 /var/lib/serval/policy.db "$1"; }
agent_field() { jq -r ".$1" /etc/serval/agent.json; }
installed_version() { dpkg-query -W -f='${Version}' serval 2> /dev/null; }
wait_active() { for _ in $(seq 1 40); do active "$1" && return 0; sleep 0.5; done; return 1; }

reset() {
    systemctl stop serval-web serval-agent > /dev/null 2>&1 || true
    dpkg --purge --force-all serval > /dev/null 2>&1 || true
    rm -rf /var/lib/serval /etc/serval /var/lib/serval-web /opt/serval /etc/pam.d/serval /run/serval /tmp/target \
        /usr/lib/serval /etc/systemd/system/serval-*.service* /etc/systemd/system/multi-user.target.wants/serval-* \
        /usr/lib/systemd/system/serval-*.service /var/lib/systemd/deb-systemd-helper-enabled/serval-* \
        /var/lib/systemd/deb-systemd-helper-enabled/multi-user.target.wants/serval-*
    pkill -x nc > /dev/null 2>&1 || true
    rm -rf /root/ops-certs /tmp/pwned
    userdel serval-web > /dev/null 2>&1 || true
    for user in alice bob; do userdel -r "$user" > /dev/null 2>&1 || true; done
    for group in serval-web opsadm extra; do groupdel "$group" > /dev/null 2>&1 || true; done
    echo PURGE | debconf-communicate serval > /dev/null 2>&1 || true
    systemctl daemon-reload > /dev/null 2>&1 || true
    systemctl reset-failed > /dev/null 2>&1 || true
}

# Everything serval-setup may change, with modes: used to prove that a rejected command changed nothing.
setup_state() {
    find /etc/serval -mindepth 1 \( -type f -o -type l -o -type d \) -exec stat -c '%n %F %a %U:%G' {} + 2> /dev/null | sort
    find /etc/serval -type f -exec sha256sum {} + 2> /dev/null | sort
}
https_fingerprint() { # host port servername
    openssl s_client -connect "$1:$2" -servername "$3" < /dev/null 2> /dev/null | openssl x509 -noout -fingerprint -sha256 2> /dev/null | sed 's/^[^=]*=//'
}
wait_for_https() {
    for _ in $(seq 1 30); do [[ -n $(https_fingerprint "$1" "$2" "$3") ]] && return 0; sleep 1; done
    return 1
}
export -f https_fingerprint wait_for_https
ensure_tools() {
    command -v curl > /dev/null 2>&1 && command -v sqlite3 > /dev/null 2>&1 && command -v nc > /dev/null 2>&1 && return 0
    apt-get install -y -qq sqlite3 iproute2 procps curl netcat-openbsd > /dev/null 2>&1
}
listens_on() { # port -> address of Web's listening socket, empty when Web does not own it
    ss -ltnpH "sport = :$1" 2> /dev/null | grep '"Serval.Web"' | awk '{print $4}' | sort | tr '\n' ' '
}
# Logs in through Web with PAM credentials; prints the session token (empty on failure).
web_session() { # user password
    local jar body token
    jar=$(mktemp)
    body=$(curl -s -c "$jar" -b "$jar" http://127.0.0.1:9080/Login)
    token=$(sed -n 's/.*<input[^>]*name="__RequestVerificationToken"[^>]*value="\([^"]*\)".*/\1/p' <<< "$body" | head -1)
    curl -s -i -c "$jar" -b "$jar" --data-urlencode "Username=$1" --data-urlencode "Password=$2" \
        --data-urlencode "__RequestVerificationToken=$token" http://127.0.0.1:9080/Login |
        sed -n 's/^[Ss]et-[Cc]ookie: __Host-ServalSession=\([^;]*\).*/\1/p' | head -1
    rm -f "$jar"
}
grants_status() { # token -> HTTP status of the grants page
    curl -s -o /dev/null -w '%{http_code}' -H "Cookie: __Host-ServalSession=$1" http://127.0.0.1:9080/Grants
}
make_expired_certificate() { # dir -> dir/expired.pem + dir/expired.key
    local dir=$1
    mkdir -p "$dir/ca"
    : > "$dir/ca/index.txt"
    echo 01 > "$dir/ca/serial"
    cat > "$dir/ca/ca.cnf" << CNF
[ca]
default_ca = CA_default
[CA_default]
database = $dir/ca/index.txt
new_certs_dir = $dir/ca
serial = $dir/ca/serial
default_md = sha256
policy = policy_any
[policy_any]
commonName = supplied
CNF
    openssl req -newkey ec -pkeyopt ec_paramgen_curve:P-256 -nodes -keyout "$dir/expired.key" -subj /CN=expired -out "$dir/expired.csr" > /dev/null 2>&1
    openssl ca -batch -selfsign -keyfile "$dir/expired.key" -in "$dir/expired.csr" -config "$dir/ca/ca.cnf" \
        -startdate 20200101000000Z -enddate 20200102000000Z -notext -out "$dir/expired.pem" > /dev/null 2>&1
}

seed_database() {
    # Stops the services so the file is quiescent, adds records, leaves the services stopped.
    systemctl stop serval-web serval-agent
    sql "INSERT INTO grants(subject_kind,subject_name,subject_id,operation,service) VALUES ('User','alice',1000,'Service.View','nginx.service');
         INSERT INTO favorites(subject_name,subject_uid,service) VALUES ('alice',1000,'nginx.service');
         INSERT INTO audit(actor_name,actor_uid,operation,service,outcome,correlation_id,timestamp_utc) VALUES ('alice',1000,'Service.View','nginx.service','Success','c-1','2026-01-01T00:00:00Z');"
}
database_dump() { sql "SELECT 'g',subject_name,operation,service FROM grants ORDER BY id; SELECT 'f',subject_name,service FROM favorites ORDER BY service; SELECT 'a',actor_name,outcome,correlation_id FROM audit ORDER BY id;"; }

# ------------------------------------------------------------------------------------------
scenario_install() {
    reset
    refute 'no .NET runtime on the host before install' bash -c 'command -v dotnet || ls /usr/share/dotnet /usr/lib/dotnet'
    apt_install "$NEW_DEB" && pass 'install succeeds on a minimal host' || { fail 'install succeeds on a minimal host'; cat "$LOG" >&2; return; }
    refute 'no .NET package was installed' bash -c 'dpkg-query -W | grep -i -E "dotnet|aspnetcore"'
    # Test tooling is installed only after the package proved it needs nothing beyond its dependencies.
    ensure_tools

    local entry
    entry=$(getent passwd serval-web)
    equals 'Web account home and shell' "$(cut -d: -f6,7 <<< "$entry")" '/nonexistent:/usr/sbin/nologin'
    assert 'Web account password is locked' bash -c "getent shadow serval-web | cut -d: -f2 | grep -q -E '^[!*]'"
    equals 'Web account has no supplementary groups' "$(id -G serval-web)" "$(id -g serval-web)"
    equals 'agent.json owner and mode' "$(stat -c '%U:%G %a' /etc/serval/agent.json)" 'root:root 600'
    equals 'agent.json names sudo' "$(agent_field adminGroup)" sudo
    equals 'agent.json GID resolved' "$(agent_field adminGid)" "$(getent group sudo | cut -d: -f3)"
    equals 'agent.json Web GID resolved' "$(agent_field webGid)" "$(id -g serval-web)"
    equals 'PAM policy mode' "$(stat -c '%U:%G %a' /etc/pam.d/serval)" 'root:root 644'
    equals 'state directory mode' "$(stat -c '%a' /var/lib/serval)" 700
    equals 'no file in the binary trees is non-root or group/other writable' \
        "$(find /opt/serval \( ! -user root -o -perm /022 \) | wc -l)" 0
    equals 'units are enabled' "$(systemctl is-enabled serval-agent serval-web | tr '\n' ' ')" 'enabled enabled '
    assert 'Agent is active' active serval-agent
    assert 'Web is active' active serval-web
    assert 'Agent socket exists' test -S /run/serval/agent.sock
    equals 'units come from the vendor directory' "$(realpath "$(systemctl show -p FragmentPath --value serval-web)")" /usr/lib/systemd/system/serval-web.service
    equals 'debconf holds the default answer' "$(debconf-show serval | sed -n 's/.*admin-group: //p')" sudo

    wait_active serval-web
    local listeners
    listeners=$(ss -ltnpH | grep '"Serval.Web"' | awk '{print $4}' | sort | tr '\n' ' ')
    equals 'Web has only the loopback listener' "$listeners" '127.0.0.1:9080 '
    assert 'Web answers on loopback' bash -c 'exec 3<>/dev/tcp/127.0.0.1/9080 && printf "GET /Login HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3 && head -1 <&3 | grep -q "200"'
    local external
    external=$(ip -4 -o addr show scope global | awk '{print $4}' | cut -d/ -f1 | head -1)
    if [[ -n $external ]]; then
        refute 'Web refuses connections on the host address' timeout 3 bash -c "exec 3<>/dev/tcp/$external/9080"
    else
        pass 'no non-loopback address to probe (skipped)'
    fi
}

scenario_preseed_and_invalid_group() {
    reset
    groupadd opsadm
    echo 'serval serval/admin-group string opsadm' | debconf-set-selections
    if apt_install "$NEW_DEB"; then pass 'preseeded group installs'; else fail 'preseeded group installs'; cat "$LOG" >&2; fi
    equals 'agent.json names the preseeded group' "$(agent_field adminGroup)" opsadm
    equals 'agent.json carries its GID' "$(agent_field adminGid)" "$(getent group opsadm | cut -d: -f3)"

    local bad
    for bad in 'no such group' 'nosuchgroup' 'root' 'Bad;Group' 'sudo/../x' ''; do
        reset
        echo "serval serval/admin-group string $bad" | debconf-set-selections
        refute "invalid group '$bad' fails the installation" apt_install "$NEW_DEB"
        assert "invalid group '$bad' is rejected by the group validation" grep -q -E 'Invalid host-admin group name|Host-admin group does not exist|invalid GID' "$LOG"
        refute "invalid group '$bad': Agent not started" active serval-agent
        refute "invalid group '$bad': Web not started" active serval-web
        refute "invalid group '$bad': no agent.json written" test -e /etc/serval/agent.json
    done
}

scenario_unsafe_account() {
    local variant
    for variant in shell password group; do
        reset
        useradd --system --user-group --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin serval-web
        case $variant in
            shell) usermod --shell /bin/bash serval-web ;;
            password) echo 'serval-web:Synthetic-1' | chpasswd ;;
            group) groupadd extra && usermod -aG extra serval-web ;;
        esac
        local before after
        before=$(getent passwd serval-web; getent shadow serval-web; id serval-web)
        refute "unsafe account ($variant): installation fails" apt_install "$NEW_DEB"
        assert "unsafe account ($variant): rejected by the identity validation" grep -q 'Dedicated Serval Web identity' "$LOG"
        after=$(getent passwd serval-web; getent shadow serval-web; id serval-web)
        equals "unsafe account ($variant): account unchanged" "$after" "$before"
        refute "unsafe account ($variant): nothing unpacked" test -e /opt/serval
        refute "unsafe account ($variant): Agent not started" active serval-agent
        refute "unsafe account ($variant): Web not started" active serval-web
    done
}

scenario_symlinked_paths() {
    local path
    for path in /etc/serval /var/lib/serval /etc/pam.d/serval; do
        reset
        mkdir -p /tmp/target
        ln -s /tmp/target "$path"
        refute "symlink at $path: installation fails" apt_install "$NEW_DEB"
        assert "symlink at $path: rejected by the symlink check" grep -q 'must not be a symbolic link' "$LOG"
        assert "symlink at $path is left in place" test -L "$path"
        equals "symlink at $path: nothing written through it" "$(find /tmp/target -mindepth 1 | wc -l)" 0
        refute "symlink at $path: nothing unpacked" test -e /opt/serval
        refute "symlink at $path: Web not started" active serval-web
    done
    reset
}

scenario_unsupported_architecture() {
    reset
    local work
    work=$(mktemp -d)
    dpkg-deb -R "$NEW_DEB" "$work/pkg"
    sed -i 's/^Architecture: .*/Architecture: armhf/' "$work/pkg/DEBIAN/control"
    dpkg-deb --root-owner-group -Znone -b "$work/pkg" "$work/serval_armhf.deb" > /dev/null
    refute 'foreign-architecture package is refused' bash -c "dpkg -i '$work/serval_armhf.deb' > '$LOG' 2>&1"
    assert 'dpkg names the architecture mismatch' grep -q 'architecture' "$LOG"
    refute 'refused package left no account' getent passwd serval-web
    refute 'refused package left no files' test -e /opt/serval
    refute 'refused package left no configuration' test -e /etc/serval
    rm -rf "$work"
}

scenario_upgrade() {
    reset
    apt_install "$OLD_DEB" || { fail 'old version installs'; cat "$LOG" >&2; return; }
    ensure_tools
    groupadd opsadm
    wait_active serval-agent
    seed_database
    local before
    before=$(database_dump)
    systemctl start serval-agent && wait_active serval-agent
    jq '.idleMinutes = 42' /etc/serval/agent.json > /etc/serval/agent.json.tmp && mv /etc/serval/agent.json.tmp /etc/serval/agent.json
    chmod 600 /etc/serval/agent.json
    serval-setup admin-group opsadm > "$LOG" 2>&1 || { fail 'serval-setup admin-group before upgrading'; cat "$LOG" >&2; }
    serval-setup listen --address 127.0.0.1 --port 9444 --tls self-signed --hostname localhost > "$LOG" 2>&1 ||
        { fail 'serval-setup listen before upgrading'; cat "$LOG" >&2; }
    equals 'configuration changed before upgrading' "$(agent_field adminGroup)/$(agent_field idleMinutes)" opsadm/42
    local setup_before
    setup_before=$(setup_state)

    if apt_install "$NEW_DEB"; then pass 'upgrade succeeds'; else fail 'upgrade succeeds'; cat "$LOG" >&2; return; fi
    equals 'serval-setup configuration (listener, TLS files, agent.json) is untouched by the upgrade' "$(setup_state)" "$setup_before"
    assert 'Web serves HTTPS on the configured listener after the upgrade' bash -c "wait_for_https 127.0.0.1 9444 localhost"
    equals 'new version installed' "$(installed_version)" "$(dpkg-deb -f "$NEW_DEB" Version)"
    local backup
    backup=$(find /var/lib/serval/backups -mindepth 1 -maxdepth 1 -type d -name "$(dpkg-deb -f "$OLD_DEB" Version)-*" | head -1)
    assert 'backup directory is named with the previous version' test -n "$backup"
    # systemd re-chowns the state directory to root:serval-web on Agent start; mode 0600 inside a 0700 directory keeps it root-only.
    equals 'backup file owner and mode' "$(stat -c '%U %a' "$backup/policy.db")" 'root 600'
    equals 'backup directory mode' "$(stat -c '%a' "$backup")" 700
    equals 'backup holds the pre-upgrade grants, favorites and audit rows' \
        "$(sqlite3 "$backup/policy.db" "SELECT 'g',subject_name,operation,service FROM grants ORDER BY id; SELECT 'f',subject_name,service FROM favorites ORDER BY service; SELECT 'a',actor_name,outcome,correlation_id FROM audit ORDER BY id;")" "$before"
    equals 'live database preserved after upgrade' "$(database_dump)" "$before"
    assert 'Agent active after upgrade' active serval-agent
    assert 'Web active after upgrade' active serval-web
    equals 'admin group and idleMinutes survive the upgrade' "$(agent_field adminGroup)/$(agent_field idleMinutes)" opsadm/42
}

scenario_backup_retention() {
    reset
    apt_install "$OLD_DEB" || { fail 'old version installs'; return; }
    wait_active serval-agent
    systemctl stop serval-web serval-agent
    local i
    mkdir -p /var/lib/serval/backups
    for i in 1 2 3 4 5 6 7; do
        mkdir -p "/var/lib/serval/backups/0.0.$i-2026010${i}T000000Z"
        touch -d "2026-01-0$i 00:00:00" "/var/lib/serval/backups/0.0.$i-2026010${i}T000000Z"
    done
    systemctl start serval-agent && wait_active serval-agent
    apt_install "$NEW_DEB" || { fail 'upgrade installs'; cat "$LOG" >&2; return; }
    equals 'only the five newest backups are kept' "$(find /var/lib/serval/backups -mindepth 1 -maxdepth 1 -type d | wc -l)" 5
    assert 'the backup of this upgrade is among them' bash -c "ls -d /var/lib/serval/backups/$(dpkg-deb -f "$OLD_DEB" Version)-*/ > /dev/null 2>&1"
    refute 'the oldest backups were pruned' test -e /var/lib/serval/backups/0.0.1-20260101T000000Z
    refute 'the oldest backups were pruned (2)' test -e /var/lib/serval/backups/0.0.3-20260103T000000Z
    assert 'the fifth newest backup is kept' test -e /var/lib/serval/backups/0.0.4-20260104T000000Z
    assert 'newer backups were kept' test -e /var/lib/serval/backups/0.0.7-20260107T000000Z
}

scenario_backup_failure() {
    reset
    apt_install "$OLD_DEB" || { fail 'old version installs'; return; }
    ensure_tools
    wait_active serval-agent
    seed_database
    local before
    before=$(database_dump)
    systemctl start serval-agent && wait_active serval-agent && systemctl start serval-web
    touch /var/lib/serval/backups   # a file where the backup directory must go: the backup cannot be written
    refute 'upgrade fails when the backup cannot be written' apt_install "$NEW_DEB"
    assert 'the failure names the backup' grep -q 'backup' "$LOG"
    equals 'previous version remains installed' "$(installed_version)" "$(dpkg-deb -f "$OLD_DEB" Version)"
    assert 'old Agent is brought back after the aborted upgrade' wait_active serval-agent
    assert 'old Web is brought back after the aborted upgrade' wait_active serval-web
    systemctl stop serval-web serval-agent
    equals 'original database unchanged' "$(database_dump)" "$before"
    rm -f /var/lib/serval/backups
}

scenario_agent_failure_after_upgrade() {
    reset
    apt_install "$OLD_DEB" || { fail 'old version installs'; return; }
    wait_active serval-agent
    echo '{}' > /etc/serval/agent.json   # Agent refuses to start on this configuration
    refute 'upgrade reports the Agent failure' apt_install "$NEW_DEB"
    assert 'the failure says Web was not started' grep -q 'serval-web.service was not started' "$LOG"
    refute 'Web is not started after an Agent failure' active serval-web
    refute 'Agent is not active' active serval-agent
    assert 'pre-upgrade backup remains available' bash -c 'ls -d /var/lib/serval/backups/*/ >/dev/null 2>&1'
    systemctl stop serval-agent > /dev/null 2>&1 || true
}

scenario_remove_purge_reinstall() {
    reset
    apt_install "$OLD_DEB" || { fail 'old version installs'; return; }
    ensure_tools
    wait_active serval-agent
    seed_database
    local before
    before=$(database_dump)
    systemctl start serval-agent
    apt_install "$NEW_DEB" || { fail 'upgrade installs'; return; }

    apt-get remove -y -qq serval > "$LOG" 2>&1 && pass 'remove succeeds' || fail 'remove succeeds'
    refute 'Agent stopped after remove' active serval-agent
    refute 'Web stopped after remove' active serval-web
    refute 'units disabled after remove' bash -c 'systemctl is-enabled serval-agent serval-web 2>/dev/null | grep -q enabled'
    refute 'binaries removed' test -e /opt/serval
    refute 'unit files removed' test -e /usr/lib/systemd/system/serval-web.service
    assert 'configuration kept after remove' test -f /etc/serval/agent.json
    assert 'database kept after remove' test -f /var/lib/serval/policy.db

    apt-get purge -y serval > "$LOG" 2>&1 && pass 'purge succeeds' || fail 'purge succeeds'
    refute '/etc/serval removed' test -e /etc/serval
    refute 'PAM policy removed' test -e /etc/pam.d/serval
    refute 'Web state directory removed' test -e /var/lib/serval-web
    equals 'database kept root-only' "$(stat -c '%U %a' /var/lib/serval/policy.db)" 'root 600'
    equals 'state directory kept root-only' "$(stat -c '%a' /var/lib/serval)" 700
    assert 'backups kept' bash -c 'ls -d /var/lib/serval/backups/*/ >/dev/null 2>&1'
    assert 'purge tells the administrator what was kept' grep -q 'kept /var/lib/serval' "$LOG"
    assert 'Web account kept' getent passwd serval-web

    apt_install "$NEW_DEB" && pass 'reinstall after purge succeeds' || { fail 'reinstall after purge succeeds'; cat "$LOG" >&2; return; }
    assert 'a database left behind is backed up before the reinstalled Agent runs' bash -c 'ls -d /var/lib/serval/backups/preinstall-*/ > /dev/null 2>&1'
    assert 'configuration recreated' test -f /etc/serval/agent.json
    assert 'Agent active after reinstall' active serval-agent
    assert 'Web active after reinstall' wait_active serval-web
    systemctl stop serval-web serval-agent
    equals 'grants, favorites and audit history preserved across purge and reinstall' "$(database_dump)" "$before"
}

scenario_source_installation_migration() {
    reset
    # State left by deploy/install-identity.sh: account, config and units copied to /etc/systemd/system.
    useradd --system --user-group --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin serval-web
    install -d -m 0755 /etc/serval
    install -d -m 0700 /var/lib/serval
    # shellcheck source=/dev/null
    (. "$REPO/deploy/serval-common.sh" && serval_write_agent_config /etc/serval/agent.json "$(id -u serval-web)" "$(id -g serval-web)" sudo "$(getent group sudo | cut -d: -f3)")
    install -m 0644 "$REPO/deploy/systemd/serval-agent.service" "$REPO/deploy/systemd/serval-web.service" /etc/systemd/system/
    systemctl daemon-reload
    if apt_install "$NEW_DEB"; then pass 'install over a source installation succeeds'; else fail 'install over a source installation succeeds'; cat "$LOG" >&2; return; fi
    refute 'old unit copies removed' test -e /etc/systemd/system/serval-agent.service
    equals 'packaged unit is in effect' "$(realpath "$(systemctl show -p FragmentPath --value serval-agent)")" /usr/lib/systemd/system/serval-agent.service
    assert 'services run from the package' active serval-agent
    equals 'existing agent.json kept' "$(agent_field adminGroup)" sudo

    reset
    useradd --system --user-group --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin serval-web
    install -m 0644 "$REPO/deploy/systemd/serval-agent.service" /etc/systemd/system/
    echo 'Environment=EXTRA=1' >> /etc/systemd/system/serval-agent.service
    refute 'an edited unit copy stops the installation' apt_install "$NEW_DEB"
    assert 'edited unit copy is left untouched' grep -q EXTRA=1 /etc/systemd/system/serval-agent.service
    assert 'the failure explains the override' grep -q 'override.conf' "$LOG"
    refute 'Web not started after the failure' active serval-web
}

install_ready() {
    reset
    apt_install "$NEW_DEB" || { fail 'package installs for the serval-setup scenarios'; cat "$LOG" >&2; return 1; }
    ensure_tools
    wait_active serval-agent && wait_active serval-web
}

scenario_setup_arguments() {
    install_ready || return
    local before rc
    before=$(setup_state)
    refute 'non-root caller is refused' setpriv --reuid=65534 --regid=65534 --clear-groups serval-setup status
    equals 'non-root caller changed nothing' "$(setup_state)" "$before"
    setpriv --reuid=65534 --regid=65534 --clear-groups serval-setup listen --loopback > "$LOG" 2>&1 || true
    assert 'non-root caller is told to use root' grep -q 'must be run as root' "$LOG"

    local -a bad=(
        '' 'frobnicate' 'status extra' 'admin-group' 'admin-group a b' 'admin-group a;b' 'admin-group $(touch /tmp/pwned)'
        'listen' 'listen --bogus 1' 'listen --loopback --loopback' 'listen --loopback --port 9081 --port 9082'
        'listen --loopback --address 127.0.0.1' 'listen --address 0.0.0.0;id --tls self-signed --hostname a.test'
        'listen --address 127.0.0.1 --port abc' 'listen --address 127.0.0.1 --port 80' 'listen --address 127.0.0.1 --port 1023'
        'listen --address 127.0.0.1 --port 65536' 'listen --address 127.0.0.1 --port 09080' 'listen --address 300.1.1.1'
        'listen --address localhost' 'listen --address 127.1' 'listen --address fe80::1%eth0 --tls self-signed --hostname a.test'
        'listen --address 0.0.0.0' 'listen --address ::' 'listen --address 192.0.2.10 --port 8443'
        'listen --address 0.0.0.0 --tls self-signed' 'listen --address 0.0.0.0 --tls self-signed --hostname a$(touch /tmp/pwned).test'
        'listen --address 0.0.0.0 --tls self-signed --hostname a_b.test' 'listen --address 0.0.0.0 --tls self-signed --hostname a..test'
        'listen --address 0.0.0.0 --tls self-signed --hostname a.test, ' 'listen --address 0.0.0.0 --tls self-signed --hostname `id`'
        'listen --address 0.0.0.0 --tls self-signed --hostname a.test --certificate /etc/hostname'
        'listen --address 0.0.0.0 --tls custom' 'listen --address 0.0.0.0 --tls custom --certificate /a --key /b --hostname a.test'
        'listen --address 0.0.0.0 --tls bogus --hostname a.test' 'listen --address 127.0.0.1 --hostname a.test'
        'listen --address 0.0.0.0 --tls custom --certificate /etc/hostname;id --key /etc/hostname'
        'listen --address 0.0.0.0 --tls self-signed --tls self-signed --hostname a.test'
    )
    local arguments
    for arguments in "${bad[@]}"; do
        # shellcheck disable=SC2086 # word splitting of the table entry is intended
        serval-setup $arguments > "$LOG" 2>&1 < /dev/null && rc=0 || rc=$?
        [[ $rc -ne 0 ]] && pass "rejected: serval-setup $arguments" || fail "accepted: serval-setup $arguments"
        equals "nothing changed by: serval-setup $arguments" "$(setup_state)" "$before"
    done
    # Control characters and an embedded newline cannot be written in the table above.
    refute 'control character in a value is rejected' serval-setup listen --address "127.0.0.1$(printf '\001')" --port 9081
    refute 'newline in a value is rejected' serval-setup listen --address "127.0.0.1
0.0.0.0" --port 9081
    refute 'newline in a group name is rejected' serval-setup admin-group "sudo
root"
    refute 'command substitution in a group is rejected' serval-setup admin-group '$(touch /tmp/pwned)'
    refute 'backticks in a group are rejected' serval-setup admin-group '`touch /tmp/pwned`'
    refute 'command substitution in a hostname is rejected' serval-setup listen --address 0.0.0.0 --tls self-signed --hostname 'a$(touch /tmp/pwned).test'
    refute 'semicolon in a hostname is rejected' serval-setup listen --address 0.0.0.0 --tls self-signed --hostname 'a.test;touch /tmp/pwned'
    refute 'command substitution in an address is rejected' serval-setup listen --address '$(touch /tmp/pwned)'
    refute 'metacharacters in a certificate path are rejected' serval-setup listen --address 127.0.0.1 --tls custom --certificate '/etc/hostname;touch /tmp/pwned' --key /etc/hostname
    equals 'control characters changed nothing' "$(setup_state)" "$before"
    refute 'no command was executed through an argument' test -e /tmp/pwned
    assert 'usage help is printed for a bad subcommand' bash -c "serval-setup frobnicate 2>&1 | grep -q '^Usage:'"
}

scenario_setup_listen_self_signed() {
    install_ready || return
    local output fingerprint
    output=$(serval-setup listen --address 0.0.0.0 --port 8443 --tls self-signed --hostname admin.example.test,192.0.2.7 2>&1) && pass 'self-signed exposure succeeds' ||
        { fail 'self-signed exposure succeeds'; echo "$output" >&2; return; }
    fingerprint=$(sed -n 's/^ *SHA256:\(.*\)$/\1/p' <<< "$output")
    assert 'the command prints the SHA-256 fingerprint' test -n "$fingerprint"
    assert 'the output contains no key material' bash -c "! grep -q 'PRIVATE KEY' <<< '$output'"
    assert 'Web is active' wait_active serval-web
    equals 'Web listens on the requested address and port' "$(listens_on 8443)" '0.0.0.0:8443 '
    assert 'Web serves HTTPS there' wait_for_https 127.0.0.1 8443 admin.example.test
    equals 'the served certificate has the printed fingerprint' "$(https_fingerprint 127.0.0.1 8443 admin.example.test)" "$fingerprint"
    equals 'certificate names are in the SAN' \
        "$(openssl x509 -in /etc/serval/tls/cert.pem -noout -ext subjectAltName | tr -d '\n ' | sed 's/.*Name:*//')" 'DNS:admin.example.test,IPAddress:192.0.2.7'
    assert 'the certificate is valid for about a year' openssl x509 -in /etc/serval/tls/cert.pem -noout -checkend $((364 * 86400))
    refute 'the certificate does not outlive 366 days' openssl x509 -in /etc/serval/tls/cert.pem -noout -checkend $((366 * 86400))
    assert 'the key is ECDSA P-256' bash -c "openssl pkey -in /etc/serval/tls/key.pem -noout -text | grep -q 'prime256v1'"
    equals 'web.json owner and mode' "$(stat -c '%U:%G %a' /etc/serval/web.json)" 'root:serval-web 640'
    equals 'key owner and mode' "$(stat -c '%U:%G %a' /etc/serval/tls/key.pem)" 'root:serval-web 640'
    equals 'certificate mode' "$(stat -c '%a' /etc/serval/tls/cert.pem)" 644
    equals 'TLS directory mode' "$(stat -c '%U:%G %a' /etc/serval/tls)" 'root:serval-web 750'
    refute 'plain HTTP is not accepted on the HTTPS port' bash -c \
        "exec 3<>/dev/tcp/127.0.0.1/8443; printf 'GET /Login HTTP/1.0\r\nHost: x\r\n\r\n' >&3; timeout 3 cat <&3 | grep -q '^HTTP/'"
    assert 'Web sends HSTS over TLS' bash -c \
        "printf 'GET /Login HTTP/1.0\r\nHost: admin.example.test\r\n\r\n' | openssl s_client -quiet -connect 127.0.0.1:8443 -servername admin.example.test 2>/dev/null | grep -qi '^Strict-Transport-Security: max-age=31536000'"
    refute 'nothing listens on the old default port any more' bash -c "exec 3<>/dev/tcp/127.0.0.1/9080"

    # Return to loopback.
    serval-setup listen --loopback > "$LOG" 2>&1 && pass 'return to loopback succeeds' || { fail 'return to loopback succeeds'; cat "$LOG" >&2; }
    assert 'Web is active on loopback' wait_active serval-web
    equals 'Web listens only on the loopback default' "$(listens_on 9080)" '127.0.0.1:9080 '
    refute 'the TLS files are removed' test -e /etc/serval/tls/key.pem
    assert 'Web answers over plain HTTP again' bash -c 'exec 3<>/dev/tcp/127.0.0.1/9080 && printf "GET /Login HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3 && head -1 <&3 | grep -q 200'
}

scenario_setup_operator_certificate() {
    install_ready || return
    mkdir -p /root/ops-certs && chmod 700 /root/ops-certs
    openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:P-256 -nodes -days 30 -keyout /root/ops-certs/k.pem \
        -out /root/ops-certs/c.pem -subj /CN=ops.example.test -addext subjectAltName=DNS:ops.example.test > /dev/null 2>&1
    local want
    want=$(openssl x509 -in /root/ops-certs/c.pem -noout -fingerprint -sha256 | sed 's/^[^=]*=//')
    serval-setup listen --address 127.0.0.1 --port 8443 --tls custom --certificate /root/ops-certs/c.pem --key /root/ops-certs/k.pem > "$LOG" 2>&1 &&
        pass 'operator certificate is accepted' || { fail 'operator certificate is accepted'; cat "$LOG" >&2; return; }
    assert 'Web serves the operator certificate' wait_for_https 127.0.0.1 8443 ops.example.test
    equals 'the served certificate is the operator certificate' "$(https_fingerprint 127.0.0.1 8443 ops.example.test)" "$want"
    equals 'key copy owner and mode' "$(stat -c '%U:%G %a' /etc/serval/tls/key.pem)" 'root:serval-web 640'
    assert 'the key copy matches the supplied key' cmp -s /root/ops-certs/k.pem /etc/serval/tls/key.pem
    assert 'the log does not contain key material' bash -c "! grep -q 'PRIVATE KEY' '$LOG'"

    # Invalid inputs: each must fail with the current configuration and TLS files untouched.
    local dir=/root/ops-certs before
    make_expired_certificate "$dir"
    openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:P-256 -nodes -days 30 -keyout "$dir/other.key" -out "$dir/other.pem" -subj /CN=other > /dev/null 2>&1
    openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -aes256 -pass pass:Synthetic1 -out "$dir/enc.key" > /dev/null 2>&1
    openssl x509 -in "$dir/c.pem" -outform DER -out "$dir/c.der"
    echo 'not a certificate' > "$dir/garbage.pem"
    ln -s "$dir/c.pem" "$dir/link.pem"
    ln -s "$dir/k.pem" "$dir/link.key"
    mkdir -p "$dir/subdir"
    before=$(setup_state)
    local case_name cert key
    while IFS='|' read -r case_name cert key; do
        serval-setup listen --address 127.0.0.1 --port 8444 --tls custom --certificate "$cert" --key "$key" > "$LOG" 2>&1 && fail "accepted: $case_name" || pass "rejected: $case_name"
        equals "unchanged after: $case_name" "$(setup_state)" "$before"
        assert "no key material printed for: $case_name" bash -c "! grep -q 'PRIVATE KEY' '$LOG'"
    done << CASES
expired certificate|$dir/expired.pem|$dir/expired.key
mismatched key|$dir/c.pem|$dir/other.key
encrypted key|$dir/other.pem|$dir/enc.key
certificate is not PEM (DER)|$dir/c.der|$dir/k.pem
certificate is garbage|$dir/garbage.pem|$dir/k.pem
key is garbage|$dir/c.pem|$dir/garbage.pem
certificate is a symbolic link|$dir/link.pem|$dir/k.pem
key is a symbolic link|$dir/c.pem|$dir/link.key
certificate is a directory|$dir/subdir|$dir/k.pem
key is a directory|$dir/c.pem|$dir/subdir
certificate is missing|$dir/missing.pem|$dir/k.pem
relative paths|c.pem|k.pem
CASES
    assert 'Web still serves the previous certificate' bash -c "[[ \$(https_fingerprint 127.0.0.1 8443 ops.example.test) == '$want' ]]"
}

scenario_setup_symlinks_and_rollback() {
    install_ready || return
    serval-setup listen --address 127.0.0.1 --port 8443 --tls self-signed --hostname localhost > "$LOG" 2>&1 || { fail 'baseline listener'; cat "$LOG" >&2; return; }
    local before fp
    before=$(setup_state)
    fp=$(https_fingerprint 127.0.0.1 8443 localhost)

    # The port is already in use: Web cannot start, the previous listener and TLS files come back.
    ensure_tools
    nc -l 127.0.0.1 8444 > /dev/null 2>&1 &
    sleep 1
    refute 'a listener change that makes Web fail exits non-zero' serval-setup listen --address 127.0.0.1 --port 8444 --tls self-signed --hostname other.test
    equals 'the previous web.json and TLS files are restored' "$(setup_state)" "$before"
    assert 'Web is running again on the previous listener' wait_for_https 127.0.0.1 8443 localhost
    equals 'with the previous certificate' "$(https_fingerprint 127.0.0.1 8443 localhost)" "$fp"
    pkill -x nc || true

    # Symbolic links under /etc/serval are never written through.
    local link
    for link in /etc/serval/web.json /etc/serval/tls/cert.pem /etc/serval/tls/key.pem /etc/serval/tls /etc/serval/agent.json; do
        mkdir -p /tmp/target
        local saved=/tmp/saved-link
        rm -rf "$saved"
        mv "$link" "$saved"
        ln -s /tmp/target/x "$link"
        # listen never touches agent.json, so only admin-group is expected to refuse that link.
        if [[ $link != /etc/serval/agent.json ]]; then
            refute "symlink at $link: listen is refused" serval-setup listen --address 127.0.0.1 --port 8445 --tls self-signed --hostname a.test
        fi
        refute "symlink at $link: admin-group is refused" serval-setup admin-group sudo
        assert "symlink at $link: nothing was written through it" test ! -e /tmp/target/x
        assert "symlink at $link is left in place" test -L "$link"
        rm -f "$link"
        mv "$saved" "$link"
    done
    equals 'configuration intact after the symlink checks' "$(setup_state)" "$before"
}

scenario_setup_admin_group() {
    install_ready || return
    groupadd opsadm
    useradd -m alice && echo 'alice:Synthetic-Pw1' | chpasswd && usermod -aG sudo alice
    useradd -m bob && echo 'bob:Synthetic-Pw2' | chpasswd && usermod -aG opsadm bob
    local alice bob
    alice=$(web_session alice Synthetic-Pw1)
    bob=$(web_session bob Synthetic-Pw2)
    assert 'both users can log in' test -n "$alice" -a -n "$bob"
    equals 'a member of the configured group administers grants' "$(grants_status "$alice")" 200
    equals 'a non-member is denied' "$(grants_status "$bob")" 403

    local before agent_since
    before=$(setup_state)
    agent_since=$(systemctl show -p ActiveEnterTimestampMonotonic --value serval-agent)
    for bad in 'nosuchgroup' 'root' 'Bad;Group' '-sudo'; do
        refute "invalid group '$bad' is refused" serval-setup admin-group "$bad"
        equals "invalid group '$bad': agent.json unchanged" "$(setup_state)" "$before"
        equals "invalid group '$bad': services were not restarted" "$(systemctl show -p ActiveEnterTimestampMonotonic --value serval-agent)" "$agent_since"
    done
    equals 'the old group still administers after the refused changes' "$(grants_status "$(web_session alice Synthetic-Pw1)")" 200

    serval-setup admin-group opsadm > "$LOG" 2>&1 && pass 'valid group is applied' || { fail 'valid group is applied'; cat "$LOG" >&2; return; }
    equals 'agent.json names the new group' "$(agent_field adminGroup)/$(agent_field adminGid)" "opsadm/$(getent group opsadm | cut -d: -f3)"
    equals 'agent.json mode' "$(stat -c '%U:%G %a' /etc/serval/agent.json)" 'root:root 600'
    assert 'the Agent was restarted' test "$(systemctl show -p ActiveEnterTimestampMonotonic --value serval-agent)" != "$agent_since"
    assert 'Agent active' wait_active serval-agent
    assert 'Web active' wait_active serval-web
    equals 'members of the previous group lose administrator rights' "$(grants_status "$(web_session alice Synthetic-Pw1)")" 403
    equals 'members of the new group gain them' "$(grants_status "$(web_session bob Synthetic-Pw2)")" 200
    equals 'the old session no longer works after the restart' "$(grants_status "$alice")" 302

    # dpkg-reconfigure uses the same path.
    echo 'serval serval/admin-group string sudo' | debconf-set-selections
    dpkg-reconfigure -f noninteractive serval > "$LOG" 2>&1 && pass 'dpkg-reconfigure applies the preseeded group' || { fail 'dpkg-reconfigure applies the preseeded group'; cat "$LOG" >&2; }
    equals 'agent.json follows dpkg-reconfigure' "$(agent_field adminGroup)" sudo
    equals 'the reconfigured group takes effect' "$(grants_status "$(web_session alice Synthetic-Pw1)")" 200
}

scenario_setup_status() {
    install_ready || return
    local output
    output=$(serval-setup status 2>&1)
    assert 'status before exposure shows plain loopback HTTP' grep -q 'http://127.0.0.1:9080' <<< "$output"
    assert 'status shows TLS none' grep -q 'TLS: *none' <<< "$output"
    assert 'status shows the admin group' grep -q 'Admin group: *sudo' <<< "$output"
    assert 'status shows both services active' bash -c "grep -q 'serval-agent: *active' <<< '$output' && grep -q 'serval-web: *active' <<< '$output'"

    serval-setup listen --address 0.0.0.0 --port 8443 --tls self-signed --hostname admin.example.test,192.0.2.7 > "$LOG" 2>&1 || { fail 'self-signed exposure for status'; cat "$LOG" >&2; return; }
    output=$(serval-setup status 2>&1)
    assert 'status shows the HTTPS listener' grep -q 'https://0.0.0.0:8443' <<< "$output"
    assert 'status shows self-signed' grep -q 'TLS: *self-signed' <<< "$output"
    assert 'status shows the host names' bash -c "grep -q 'admin.example.test' <<< '$output' && grep -q '192.0.2.7' <<< '$output'"
    assert 'status shows the expiry' grep -q 'Expires:' <<< "$output"
    equals 'status shows the certificate fingerprint' "$(sed -n 's/^Fingerprint: *SHA256://p' <<< "$output")" "$(openssl x509 -in /etc/serval/tls/cert.pem -noout -fingerprint -sha256 | sed 's/^[^=]*=//')"
    refute 'status prints no key material' grep -q -e 'PRIVATE KEY' -e 'BEGIN' <<< "$output"
    refute 'status does not leak the key bytes' bash -c "grep -qF \"\$(sed -n '2p' /etc/serval/tls/key.pem)\" <<< '$output'"
}

scenario_keyring_refresh() {
    # A package built for release carries the repository keyring; postinst refreshes the copy that
    # install.sh left at the keyrings path. Built here by repacking the test package.
    local work keyring=/usr/share/keyrings/serval-archive-keyring.gpg
    work=$(mktemp -d)
    dpkg-deb -R "$NEW_DEB" "$work/pkg"
    mkdir -p "$work/pkg/usr/share/serval"
    printf 'packaged-key' > "$work/pkg/usr/share/serval/serval-archive-keyring.gpg"
    chmod 0644 "$work/pkg/usr/share/serval/serval-archive-keyring.gpg"
    dpkg-deb --root-owner-group -b "$work/pkg" "$work/with-keyring.deb" > /dev/null

    reset
    rm -f "$keyring"
    apt_install "$work/with-keyring.deb" || { fail 'the package with a keyring installs'; cat "$LOG" >&2; return; }
    equals 'the keyring is packaged at its own path' "$(cat /usr/share/serval/serval-archive-keyring.gpg)" packaged-key
    refute 'a host not set up from the repository gets no keyring copy' test -e "$keyring"

    reset
    printf 'stale-key' > "$keyring"
    chmod 0600 "$keyring"
    apt_install "$work/with-keyring.deb" || { fail 'the package refreshes an existing keyring copy'; cat "$LOG" >&2; return; }
    equals 'an existing keyring copy is refreshed' "$(cat "$keyring")" packaged-key
    equals 'the refreshed keyring is root-owned and world-readable' "$(stat -c '%U:%G %a' "$keyring")" 'root:root 644'
    refute 'the keyring copy is not owned by the package' dpkg -S "$keyring"

    reset
    printf 'stale-key' > "$work/elsewhere"
    ln -sf "$work/elsewhere" "$keyring"
    apt_install "$work/with-keyring.deb" || { fail 'a symlinked keyring path does not stop the installation'; cat "$LOG" >&2; return; }
    equals 'a symlinked keyring path is left alone' "$(cat "$work/elsewhere")" stale-key
    rm -f "$keyring"
    rm -rf "$work"
}

scenario_setup_certificate_chain() {
    install_ready || return
    local dir=/root/ops-certs
    mkdir -p "$dir" && chmod 700 "$dir"
    openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:P-256 -nodes -days 30 -keyout "$dir/root.key" -out "$dir/root.pem" \
        -subj /CN=Test-Root -addext basicConstraints=critical,CA:TRUE > /dev/null 2>&1
    openssl req -new -newkey ec -pkeyopt ec_paramgen_curve:P-256 -nodes -keyout "$dir/int.key" -out "$dir/int.csr" -subj /CN=Test-Intermediate > /dev/null 2>&1
    openssl x509 -req -in "$dir/int.csr" -CA "$dir/root.pem" -CAkey "$dir/root.key" -CAcreateserial -days 30 -out "$dir/int.pem" \
        -extfile <(printf 'basicConstraints=critical,CA:TRUE\n') > /dev/null 2>&1
    openssl req -new -newkey ec -pkeyopt ec_paramgen_curve:P-256 -nodes -keyout "$dir/leaf.key" -out "$dir/leaf.csr" -subj /CN=chain.example.test > /dev/null 2>&1
    openssl x509 -req -in "$dir/leaf.csr" -CA "$dir/int.pem" -CAkey "$dir/int.key" -CAcreateserial -days 30 -out "$dir/leaf.pem" \
        -extfile <(printf 'subjectAltName=DNS:chain.example.test\n') > /dev/null 2>&1
    cat "$dir/leaf.pem" "$dir/int.pem" > "$dir/fullchain.pem"
    serval-setup listen --address 127.0.0.1 --port 8443 --tls custom --certificate "$dir/fullchain.pem" --key "$dir/leaf.key" > "$LOG" 2>&1 &&
        pass 'a certificate chain is accepted' || { fail 'a certificate chain is accepted'; cat "$LOG" >&2; return; }
    assert 'Web serves the chain' wait_for_https 127.0.0.1 8443 chain.example.test
    local sent
    sent=$(openssl s_client -connect 127.0.0.1:8443 -servername chain.example.test -showcerts < /dev/null 2> /dev/null | grep -c 'BEGIN CERTIFICATE')
    equals 'Web sends the leaf and the intermediate' "$sent" 2
    equals 'the served leaf is the first certificate of the file' "$(https_fingerprint 127.0.0.1 8443 chain.example.test)" \
        "$(openssl x509 -in "$dir/leaf.pem" -noout -fingerprint -sha256 | sed 's/^[^=]*=//')"
    equals 'the installed certificate file holds only certificates' "$(grep -c '^-----BEGIN' /etc/serval/tls/cert.pem)" 2
    assert 'the installed certificate file holds no key block' bash -c '! grep -q "PRIVATE KEY" /etc/serval/tls/cert.pem'

    # Rejected with the configuration untouched: a file above Web's 16 KiB limit and a "certificate" that
    # also carries the private key (it would be installed world-readable).
    local before
    before=$(setup_state)
    { cat "$dir/fullchain.pem"; head -c 20000 /dev/zero | tr '\0' '#'; echo; } > "$dir/huge.pem"
    serval-setup listen --address 127.0.0.1 --port 8443 --tls custom --certificate "$dir/huge.pem" --key "$dir/leaf.key" > "$LOG" 2>&1 &&
        fail 'a certificate file above 16 KiB is refused' || pass 'a certificate file above 16 KiB is refused'
    equals 'configuration unchanged after the oversized file' "$(setup_state)" "$before"
    cat "$dir/leaf.pem" "$dir/leaf.key" > "$dir/combined.pem"
    serval-setup listen --address 127.0.0.1 --port 8443 --tls custom --certificate "$dir/combined.pem" --key "$dir/leaf.key" > "$LOG" 2>&1 &&
        fail 'a certificate file that contains the private key is refused' || pass 'a certificate file that contains the private key is refused'
    assert 'the refusal names the problem' grep -q 'only certificates' "$LOG"
    equals 'configuration unchanged after the combined file' "$(setup_state)" "$before"
}

scenario_remove_then_install() {
    # remove (without purge) followed by install must leave both services enabled and running.
    reset
    apt_install "$NEW_DEB" || { fail 'package installs'; cat "$LOG" >&2; return; }
    ensure_tools
    wait_active serval-agent && wait_active serval-web
    apt-get remove -y -qq serval > "$LOG" 2>&1 && pass 'remove succeeds' || fail 'remove succeeds'
    apt_install "$NEW_DEB" && pass 'install after remove succeeds' || { fail 'install after remove succeeds'; cat "$LOG" >&2; return; }
    equals 'units are enabled again' "$(systemctl is-enabled serval-agent serval-web | tr '\n' ' ')" 'enabled enabled '
    assert 'Agent active after install' wait_active serval-agent
    assert 'Web active after install' wait_active serval-web
}

all=(install preseed_and_invalid_group unsafe_account symlinked_paths unsupported_architecture upgrade backup_failure
    backup_retention agent_failure_after_upgrade remove_purge_reinstall source_installation_migration
    setup_arguments setup_listen_self_signed setup_operator_certificate setup_symlinks_and_rollback setup_admin_group setup_status
    keyring_refresh setup_certificate_chain remove_then_install)
if (( $# > 0 )); then all=("$@"); fi
for name in "${all[@]}"; do
    echo "== $name"
    "scenario_$name"
done
reset
if (( failures > 0 )); then
    echo "$failures check(s) failed." >&2
    exit 1
fi
echo 'All package checks passed.'
