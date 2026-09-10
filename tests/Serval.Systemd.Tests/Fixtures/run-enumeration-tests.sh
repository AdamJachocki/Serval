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
instance_alias="$root/$prefix-helper@installed.service"
template_alias="$root/$prefix-helper@.service"
failed="$root/$prefix-failed.service"
masked="$root/$prefix-masked.service"
for file in "$inactive" "$alias" "$template" "$instance" "$instance_alias" "$template_alias" "$failed" "$masked"; do
    test ! -e "$file" && test ! -L "$file"
done
cleanup() {
    systemctl stop "$prefix-transient.service" "$prefix-worker@loaded.service" || true
    systemctl reset-failed "$prefix-failed.service" || true
    rm -f -- "$inactive" "$alias" "$template" "$instance" "$instance_alias" "$template_alias" "$failed" "$masked"
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
ln -s "$prefix-worker@installed.service" "$instance_alias"
ln -s "$prefix-worker@.service" "$template_alias"
cat > "$failed" <<'UNIT'
[Unit]
Description=Serval inspection disposable failed fixture
[Service]
Type=oneshot
ExecStart=/usr/bin/false
UNIT
ln -s /dev/null "$masked"
systemctl daemon-reload
if systemctl start "$prefix-failed.service"; then
    echo "The failed-service fixture unexpectedly succeeded." >&2
    exit 1
fi
systemctl start "$prefix-worker@loaded.service"
systemd-run --quiet --unit="$prefix-transient.service" /usr/bin/sleep 300
export SERVAL_REAL_SYSTEMD_TESTS=1
"$@"
