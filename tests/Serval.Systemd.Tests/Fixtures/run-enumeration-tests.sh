#!/bin/bash
# Test harness only; production discovery never executes processes or writes units.
set -euo pipefail
export SERVAL_ENUMERATION_FIXTURE_PREFIX="serval-enumeration-test-$(cat /proc/sys/kernel/random/uuid)"
prefix="$SERVAL_ENUMERATION_FIXTURE_PREFIX"
[[ "$prefix" =~ ^serval-enumeration-test-[a-f0-9-]+$ ]]
instance_id="${prefix#serval-enumeration-test-}"
root=/run/systemd/system
inactive="$root/$prefix-inactive.service"
alias="$root/$prefix-alias.service"
template="$root/$prefix-worker@.service"
instance="$root/$prefix-worker@installed.service"
instance_alias="$root/$prefix-helper@installed.service"
template_alias="$root/$prefix-helper@.service"
privileged_name="serval-agent@$instance_id.service"
privileged="$root/$privileged_name"
privileged_alias_name="serval-exclusion-alias@$instance_id.service"
privileged_alias="$root/$privileged_alias_name"
lookalike_name="serval-agent-helper@$instance_id.service"
lookalike="$root/$lookalike_name"
failed="$root/$prefix-failed.service"
masked="$root/$prefix-masked.service"
for name in "$privileged_name" "$privileged_alias_name" "$lookalike_name"; do
    test -z "$(systemctl list-unit-files --no-legend --plain "$name")"
    test -z "$(systemctl list-units --all --no-legend --plain "$name")"
done
for file in "$inactive" "$alias" "$template" "$instance" "$instance_alias" "$template_alias" "$privileged" "$privileged_alias" "$lookalike" "$failed" "$masked"; do
    test ! -e "$file" && test ! -L "$file"
done
cleanup() {
    systemctl stop "$prefix-transient.service" "$prefix-worker@loaded.service" || true
    systemctl reset-failed "$prefix-failed.service" || true
    rm -f -- "$inactive" "$alias" "$template" "$instance" "$instance_alias" "$template_alias" "$privileged" "$privileged_alias" "$lookalike" "$failed" "$masked"
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
cat > "$privileged" <<'UNIT'
[Unit]
Description=Serval privileged-unit exclusion fixture
[Service]
Type=oneshot
ExecStart=/usr/bin/true
UNIT
cat > "$lookalike" <<'UNIT'
[Unit]
Description=Serval privileged-unit exclusion negative fixture
[Service]
Type=oneshot
ExecStart=/usr/bin/true
UNIT
ln -s "$prefix-inactive.service" "$alias"
ln -s "$prefix-worker@.service" "$instance"
ln -s "$prefix-worker@installed.service" "$instance_alias"
ln -s "$prefix-worker@.service" "$template_alias"
ln -s "$privileged_name" "$privileged_alias"
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
