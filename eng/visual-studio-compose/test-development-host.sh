#!/bin/sh
# Runs only inside a disposable smoke container, never in the developer's F5 container.
set -eu
check=/opt/serval/development/verify-development-host.sh
initialize=/opt/serval/development/initialize-demos.sh
fail() { printf 'Development smoke failed: %s\n' "$1" >&2; exit 1; }
negative=0
reject() {
    negative=$((negative + 1))
    result=0
    "$@" >/dev/null 2>&1 || result=$?
    [ "$result" -eq 1 ] || fail "negative check $negative returned $result instead of a bounded rejection"
}

# A test-only stand-in checks the bootstrap's kernel identity before a debugger
# could launch Web. Real Visual Studio/vsdbg acceptance is recorded separately.
mkdir -p /opt/serval/vsdbg/linux-arm64
cat >/opt/serval/vsdbg/vsdbg <<'EOF'
#!/bin/sh
set -eu
[ "$(id -u)" = "$(id -u serval-web)" ]
[ "$(id -g)" = "$(id -g serval-web)" ]
[ "$(id -G)" = "$(id -g serval-web)" ]
! grep -E '^Cap(Inh|Prm|Eff|Bnd|Amb):' /proc/self/status | grep -qv '0000000000000000$'
[ -w /dev/console ] && [ ! -r /dev/console ]
: >/dev/console
EOF
chmod 0755 /opt/serval/vsdbg/vsdbg
cp /opt/serval/vsdbg/vsdbg /opt/serval/vsdbg/linux-arm64/vsdbg
/opt/serval/development/start-debugger.sh --interpreter=vscode
reject /opt/serval/development/start-debugger.sh --arbitrary-argument
chown serval-web /dev/console
reject /opt/serval/development/start-debugger.sh --interpreter=vscode
chown root /dev/console

timeout 90s /opt/serval/development/start-agent.sh
"$check"
"$initialize" # Idempotence must preserve all three states.
[ "$(stat -c %u:%g:%a /etc/systemd/system/serval-demo-running.service)" = 0:0:644 ] || fail unit-owner
pid=$(systemctl show serval-demo-running.service --property=MainPID --value)
[ "$(awk '/^Uid:/ {print $2}' "/proc/$pid/status")" = "$(id -u serval-demo)" ] || fail workload-identity
awk 'NR > 1 && $4 == "0A" {exit 1}' "/proc/$pid/net/tcp" "/proc/$pid/net/tcp6" || fail workload-listener
[ "$(getent shadow serval-demo | cut -d: -f2)" = '!' ] || fail workload-unlocked

timeout 5s systemctl stop serval-demo-running.service
reject "$check" --health
[ "$(systemctl show serval-demo-running.service --property=ActiveState --value)" = inactive ] || fail health-mutated-state
timeout 5s systemctl start serval-demo-running.service
mv /etc/systemd/system/serval-demo-inactive.service /tmp/serval-demo-inactive.saved
timeout 5s systemctl daemon-reload
reject "$check" --health
mv /tmp/serval-demo-inactive.saved /etc/systemd/system/serval-demo-inactive.service
timeout 5s systemctl daemon-reload
timeout 5s systemctl stop serval-agent.service
reject "$check" --health
timeout 90s /opt/serval/development/start-agent.sh
"$check"

# A fresh initialization must reject canonical and alias collisions manager-wide.
rm /var/lib/serval-development/initialized
reject "$initialize"
timeout 5s systemctl stop serval-demo-running.service
timeout 5s systemctl reset-failed serval-demo-failed.service
for name in running inactive failed; do rm "/etc/systemd/system/serval-demo-$name.service"; done
timeout 5s systemctl daemon-reload
ln -s /etc/systemd/system/serval-agent.service /usr/lib/systemd/system/serval-demo-running.service
timeout 5s systemctl daemon-reload
reject "$initialize"
rm /usr/lib/systemd/system/serval-demo-running.service
timeout 5s systemctl daemon-reload
ln -s /does-not-exist /etc/systemd/system/serval-demo-running.service
reject "$initialize"
rm /etc/systemd/system/serval-demo-running.service
"$initialize"
"$check"

# Agent startup rejects runtime substitution and a missing binary.
timeout 5s systemctl stop serval-agent.service
rm -rf /run/serval
ln -s /tmp /run/serval
reject /opt/serval/development/start-agent.sh
rm /run/serval
mv /opt/serval/agent/Serval.Agent /opt/serval/agent/Serval.Agent.saved
reject /opt/serval/development/start-agent.sh
mv /opt/serval/agent/Serval.Agent.saved /opt/serval/agent/Serval.Agent
timeout 90s /opt/serval/development/start-agent.sh
"$check"
/opt/serval/development/stop-web.sh
/opt/serval/development/stop-web.sh
"$check" --health
reject "$check"
# The manager socket is unavailable: checks must fail within their deadlines.
timeout 5s systemctl stop dbus.service dbus.socket
mv /run/systemd/private /run/systemd/private.saved
reject "$check" --health
reject timeout 40s /opt/serval/development/start-agent.sh
mv /run/systemd/private.saved /run/systemd/private
printf 'Development runtime and negative checks passed.\n'
