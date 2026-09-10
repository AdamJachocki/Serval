#!/bin/bash
# Test harness only; production discovery never executes processes or writes units.
set -euo pipefail
export SERVAL_ENUMERATION_FIXTURE_PREFIX="serval-enumeration-test-$(cat /proc/sys/kernel/random/uuid)"
prefix="$SERVAL_ENUMERATION_FIXTURE_PREFIX"
[[ "$prefix" =~ ^serval-enumeration-test-[a-f0-9-]+$ ]]
root=/run/systemd/system
inactive="$root/$prefix-inactive.service"
alias="$root/$prefix-alias.service"
template="$root/$prefix-worker@.service"
instance="$root/$prefix-worker@installed.service"
for file in "$inactive" "$alias" "$template" "$instance"; do
    test ! -e "$file" && test ! -L "$file"
done
cleanup() {
    systemctl stop "$prefix-transient.service" "$prefix-worker@loaded.service" || true
    rm -f -- "$inactive" "$alias" "$template" "$instance"
    systemctl daemon-reload
}
trap cleanup EXIT
cat > "$inactive" <<'UNIT'
[Unit]
Description=Serval enumeration disposable inactive fixture
[Service]
Type=oneshot
ExecStart=/usr/bin/true
UNIT
cat > "$template" <<'UNIT'
[Unit]
Description=Serval enumeration disposable instance fixture
[Service]
ExecStart=/usr/bin/sleep 300
UNIT
ln -s "$prefix-inactive.service" "$alias"
ln -s "$prefix-worker@.service" "$instance"
systemctl daemon-reload
systemctl start "$prefix-worker@loaded.service"
systemd-run --quiet --unit="$prefix-transient.service" /usr/bin/sleep 300
export SERVAL_REAL_SYSTEMD_TESTS=1
"$@"
