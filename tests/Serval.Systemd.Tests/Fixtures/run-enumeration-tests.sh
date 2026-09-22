#!/bin/bash
# Test harness only; production discovery never executes processes or writes units.
set -Eeuo pipefail
report_error() {
    local status="$?"
    printf 'Harness command failed at line %s with status %s: %s\n' \
        "$1" "$status" "$2" >&2
    return "$status"
}
trap 'report_error "$LINENO" "$BASH_COMMAND"' ERR
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
environment_name="$prefix-environment@sample.service"
environment_unit="$root/$environment_name"
environment_dropin="$environment_unit.d"
environment_file_name="$prefix-environment-file.service"
environment_file_unit="$root/$environment_file_name"
environment_file_source="$root/$prefix-environment-file.env"
environment_invalid_name="$prefix-environment-invalid.service"
environment_invalid_unit="$root/$environment_invalid_name"
environment_invalid_source="$root/$prefix-environment-invalid.env"
environment_divergence_template_name="$prefix-environment-divergence@.service"
environment_divergence_template="$root/$environment_divergence_template_name"
environment_divergence_lf="$root/$prefix-environment-divergence-lf.env"
environment_divergence_cr="$root/$prefix-environment-divergence-cr.env"
environment_divergence_crlf="$root/$prefix-environment-divergence-crlf.env"
source_template_name="$prefix-source@.service"
source_template="$root/$source_template_name"
source_dropin="$source_template.d"
source_name="$prefix-source@sample.service"
source_alias_name="$prefix-source-alias@sample.service"
source_alias="$root/$source_alias_name"
source_one="$root/$prefix-source-sample-one.env"
source_two="$root/$prefix-source-sample-two.env"
source_missing="$root/$prefix-source-sample-optional-missing.env"
source_required_name="$prefix-source-required-missing.service"
source_required_unit="$root/$source_required_name"
source_required_missing="$root/$prefix-source-required-missing.env"
source_unsupported_name="$prefix-source-unsupported.service"
source_unsupported_unit="$root/$source_unsupported_name"
source_disappear_name="$prefix-source-disappear.service"
source_disappear_unit="$root/$source_disappear_name"
fixture_names=(
    "$environment_name"
    "$environment_file_name"
    "$environment_invalid_name"
    "$environment_divergence_template_name"
    "$prefix-environment-divergence@lf.service"
    "$prefix-environment-divergence@cr.service"
    "$prefix-environment-divergence@crlf.service"
    "$prefix-inactive.service"
    "$prefix-alias.service"
    "$prefix-worker@.service"
    "$prefix-worker@installed.service"
    "$prefix-helper@installed.service"
    "$prefix-helper@.service"
    "$prefix-worker@loaded.service"
    "$prefix-transient.service"
    "$privileged_name"
    "$privileged_alias_name"
    "$lookalike_name"
    "$prefix-failed.service"
    "$prefix-masked.service"
    "$source_template_name"
    "$source_name"
    "$source_alias_name"
    "$source_required_name"
    "$source_unsupported_name"
    "$source_disappear_name"
)
is_listed() {
    local target="$1"
    local listing="$2"
    local line
    while IFS= read -r line; do
        if [[ "${line%% *}" == "$target" ]]; then
            return 0
        fi
    done <<< "$listing"
    return 1
}
unit_files_before="$(systemctl list-unit-files --all --no-legend --plain --type=service)"
loaded_units_before="$(systemctl list-units --all --no-legend --plain --type=service)"
for name in "${fixture_names[@]}"; do
    if is_listed "$name" "$unit_files_before" || is_listed "$name" "$loaded_units_before"; then
        printf '%s: fixture identity already exists in the systemd manager\n' "$name" >&2
        exit 1
    fi
done
for file in "$inactive" "$alias" "$template" "$instance" "$instance_alias" "$template_alias" "$privileged" "$privileged_alias" "$lookalike" "$failed" "$masked" "$environment_unit" "$environment_dropin" "$environment_file_unit" "$environment_file_source" "$environment_invalid_unit" "$environment_invalid_source" "$environment_divergence_template" "$environment_divergence_lf" "$environment_divergence_cr" "$environment_divergence_crlf" "$source_template" "$source_dropin" "$source_alias" "$source_one" "$source_two" "$source_missing" "$source_required_unit" "$source_required_missing" "$source_unsupported_unit" "$source_disappear_unit"; do
    test ! -e "$file" && test ! -L "$file"
done
cleanup() {
    systemctl stop "$environment_file_name" "$environment_invalid_name" \
        "$prefix-environment-divergence@lf.service" "$prefix-environment-divergence@cr.service" \
        "$prefix-environment-divergence@crlf.service" || true
    systemctl stop "$prefix-transient.service" "$prefix-worker@loaded.service" || true
    systemctl reset-failed "$prefix-failed.service" || true
    rm -f -- "$inactive" "$alias" "$template" "$instance" "$instance_alias" "$template_alias" "$privileged" "$privileged_alias" "$lookalike" "$failed" "$masked"
    rm -f -- "$environment_unit" "$environment_dropin/10-environment.conf" "$environment_dropin/expected"
    rm -f -- "$environment_file_unit" "$environment_file_source" "$environment_invalid_unit" \
        "$environment_invalid_source" "$environment_divergence_template" "$environment_divergence_lf" \
        "$environment_divergence_cr" "$environment_divergence_crlf"
    rm -f -- "$source_template" "$source_dropin/10-manager.conf" \
        "$source_dropin/20-sources.conf" "$source_dropin/30-repeat.conf" "$source_alias" \
        "$source_one" "$source_two" "$source_required_unit" "$source_unsupported_unit" \
        "$source_disappear_unit"
    rmdir -- "$environment_dropin" || true
    rmdir -- "$source_dropin" || true
    systemctl daemon-reload
}
trap cleanup EXIT
# Private synthetic values; never print them or pass them as process arguments.
mkdir -m 700 -- "$environment_dropin"
(
    umask 077
    marker="$(cat /proc/sys/kernel/random/uuid)"
    printf '%s' "$marker" > "$environment_dropin/expected"
    printf '[Service]\nType=oneshot\nExecStart=/usr/bin/true\nEnvironment=REMOVED=%s\n' "$marker" > "$environment_unit"
    {
        printf '[Service]\nEnvironment=\nEnvironment="VALUE=%s first"\n' "$marker"
        printf 'Environment="VALUE=%s final" "EMPTY="\n' "$marker"
        printf 'Environment="ESCAPED=%s\\n\\t\\\\"\n' "$marker"
        printf 'Environment="LITERAL=%s $HOME $(id) `id`"\n' "$marker"
        printf 'Environment="SPECIFIER=%%n/%%i/%%%%"\n'
        # Invalid assignment contains no value, even if systemd journals its diagnostic.
        printf 'Environment=9INVALID=\n'
    } > "$environment_dropin/10-environment.conf"
)
# Ordered source-reader fixture. Private values are generated and never emitted.
mkdir -m 700 -- "$source_dropin"
(
    umask 077
    marker="$(cat /proc/sys/kernel/random/uuid)"
    printf 'ONE=%s\n' "$marker" > "$source_one"
    printf 'TWO=%s\n' "$marker" > "$source_two"
    cat > "$source_template" <<UNIT
[Unit]
Description=Serval ordered source reader fixture
[Service]
Type=oneshot
Environment=REMOVED=$marker
EnvironmentFile=$source_two
ExecStart=/usr/bin/true
UNIT
    cat > "$source_dropin/10-manager.conf" <<UNIT
[Service]
Environment=
Environment=ACTIVE=$marker-%%i
UNIT
    cat > "$source_dropin/20-sources.conf" <<UNIT
[Service]
EnvironmentFile=
EnvironmentFile=$source_one
EnvironmentFile=-$source_missing
EnvironmentFile=$source_two
UNIT
    cat > "$source_dropin/30-repeat.conf" <<UNIT
[Service]
EnvironmentFile=$source_one
UNIT
)
ln -s "$source_name" "$source_alias"
cat > "$source_required_unit" <<UNIT
[Unit]
Description=Serval required missing source fixture
[Service]
Type=oneshot
EnvironmentFile=$source_required_missing
ExecStart=/usr/bin/true
UNIT
cat > "$source_unsupported_unit" <<UNIT
[Unit]
Description=Serval unsupported source fixture
[Service]
Type=oneshot
UnsetEnvironment=ACTIVE
EnvironmentFile=/dev/null
ExecStart=/usr/bin/true
UNIT
cat > "$source_disappear_unit" <<'UNIT'
[Unit]
Description=Serval disappearing source fixture
[Service]
Type=oneshot
ExecStart=/usr/bin/true
UNIT
# Private EnvironmentFile= parser oracle. Values are read only from /proc by the test and are never printed.
(
    umask 077
    marker="$(cat /proc/sys/kernel/random/uuid)"
    {
        printf '# ordinary LF comment\n'
        printf '; ordinary CR comment\r'
        printf '# ordinary CRLF comment\r\n'
        printf 'ignored non-assignment line\n'
        printf 'EMPTY=\n'
        printf 'UNQUOTED=  %s unquoted\\ value  \n' "$marker"
        printf "SINGLE='%s single\nline'\n" "$marker"
        printf 'DOUBLE="%s \\$HOME \\` \\\\ \\" \\q \\\ncontinued"\n' "$marker"
        printf 'LITERAL=%s $HOME ${HOME} $(id) `id` %%n %%%% ==\n' "$marker"
        printf 'UNICODE=%s-zażółć-😀\n' "$marker"
        printf 'DUPLICATE=%s-first\nDUPLICATE=%s-final\n' "$marker" "$marker"
    } > "$environment_file_source"
    printf 'FORBIDDEN=\uFDD0\n' > "$environment_invalid_source"
    printf '# divergent \\\nAFTER=%s\n' "$marker" > "$environment_divergence_lf"
    printf '# divergent \\\rAFTER=%s\r' "$marker" > "$environment_divergence_cr"
    printf '# stable \\\r\nAFTER=%s\r\n' "$marker" > "$environment_divergence_crlf"
)
cat > "$environment_file_unit" <<UNIT
[Unit]
Description=Serval environment-file parser fixture
[Service]
Type=simple
EnvironmentFile=$environment_file_source
ExecStart=/usr/bin/sleep 300
UNIT
cat > "$environment_invalid_unit" <<UNIT
[Unit]
Description=Serval invalid environment-file parser fixture
[Service]
Type=simple
EnvironmentFile=$environment_invalid_source
ExecStart=/usr/bin/sleep 300
UNIT
cat > "$environment_divergence_template" <<UNIT
[Unit]
Description=Serval environment-file divergence fixture
[Service]
Type=simple
EnvironmentFile=$root/$prefix-environment-divergence-%i.env
ExecStart=/usr/bin/sleep 300
UNIT
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
assert_state() {
    local unit="$1"
    local property
    local actual
    shift
    for property in LoadState ActiveState SubState; do
        actual="$(systemctl show --no-pager --property="$property" --value "$unit")"
        if [[ "$actual" != "$1" ]]; then
            printf '%s: expected %s=%s, got %s\n' "$unit" "$property" "$1" "$actual" >&2
            return 1
        fi
        shift
    done
}
assert_not_loaded() {
    local loaded
    loaded="$(systemctl list-units --all --no-legend --plain --type=service)"
    if is_listed "$1" "$loaded"; then
        printf '%s: expected unit to remain absent from the loaded-unit set\n' "$1" >&2
        return 1
    fi
}
assert_installed_fixtures_unloaded() {
    assert_not_loaded "$prefix-inactive.service"
    assert_not_loaded "$prefix-alias.service"
    assert_not_loaded "$prefix-worker@installed.service"
    assert_not_loaded "$prefix-helper@installed.service"
    assert_not_loaded "$privileged_name"
    assert_not_loaded "$privileged_alias_name"
    assert_not_loaded "$lookalike_name"
    assert_not_loaded "$prefix-masked.service"
}
assert_templates_unloaded() {
    assert_not_loaded "$prefix-worker@.service"
    assert_not_loaded "$prefix-helper@.service"
}
assert_runtime_states() {
    assert_state "$prefix-worker@loaded.service" loaded active running
    assert_state "$prefix-transient.service" loaded active running
    assert_state "$prefix-failed.service" loaded failed failed
}
assert_discovery_states() {
    assert_state "$prefix-inactive.service" loaded inactive dead
    assert_state "$prefix-alias.service" loaded inactive dead
    assert_state "$prefix-worker@installed.service" loaded inactive dead
    assert_state "$prefix-helper@installed.service" loaded inactive dead
    assert_state "$privileged_name" loaded inactive dead
    assert_state "$privileged_alias_name" loaded inactive dead
    assert_state "$lookalike_name" loaded inactive dead
    assert_state "$prefix-masked.service" masked inactive dead
    assert_runtime_states
}
snapshot_runtime_lifecycle() {
    local unit
    local property
    for unit in "$prefix-worker@loaded.service" "$prefix-transient.service" "$prefix-failed.service"; do
        printf '%s\n' "$unit"
        for property in InvocationID StateChangeTimestampMonotonic ActiveEnterTimestampMonotonic \
            InactiveEnterTimestampMonotonic ExecMainStartTimestampMonotonic ExecMainExitTimestampMonotonic; do
            printf '%s=%s\n' "$property" \
                "$(systemctl show --no-pager --property="$property" --value "$unit")"
        done
    done
}
snapshot_inactive_journal() {
    local unit
    for unit in "$prefix-inactive.service" "$prefix-worker@installed.service" \
        "$privileged_name" "$lookalike_name" "$prefix-masked.service"; do
        printf '%s\n' "$unit"
        journalctl --quiet --unit="$unit" --no-pager --output=short-monotonic
    done
}
assert_unchanged() {
    local description="$1"
    local before="$2"
    local after="$3"
    if [[ "$after" != "$before" ]]; then
        printf '%s changed during discovery:\n' "$description" >&2
        diff -u <(printf '%s\n' "$before") <(printf '%s\n' "$after") >&2 || true
        return 1
    fi
}
assert_installed_fixtures_unloaded
assert_templates_unloaded
assert_runtime_states
lifecycle_before="$(snapshot_runtime_lifecycle)"
journal_before="$(snapshot_inactive_journal)"
export SERVAL_REAL_SYSTEMD_TESTS=1
"$@"
assert_discovery_states
assert_templates_unloaded
lifecycle_after="$(snapshot_runtime_lifecycle)"
journal_after="$(snapshot_inactive_journal)"
assert_unchanged "Runtime lifecycle properties" "$lifecycle_before" "$lifecycle_after"
assert_unchanged "Inactive fixture journal" "$journal_before" "$journal_after"
