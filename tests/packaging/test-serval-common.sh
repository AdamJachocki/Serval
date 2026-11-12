#!/bin/sh
# Tests deploy/serval-common.sh. Run as root (atomic writes chown to root:root), on Linux:
#   sh tests/packaging/test-serval-common.sh      (also run with bash)
# The snippets passed to run_lib are deliberately single-quoted: they expand inside eval.
# shellcheck disable=SC2016
set -eu

if [ "$(id -u)" != 0 ]; then
    echo 'Run as root.' >&2
    exit 2
fi
root=$(cd -- "$(dirname -- "$0")/../.." && pwd)
lib=$root/deploy/serval-common.sh
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
mkdir "$work/bin" "$work/etc"
cat > "$work/bin/getent" <<'SCRIPT'
#!/bin/sh
[ "$1" = group ] || exit 2
case "$2" in
    sudo) echo 'sudo:x:27:alice' ;;
    wheel) echo 'wheel:x:10:' ;;
    ops_team-1) echo 'ops_team-1:x:2001:' ;;
    root) echo 'root:x:0:' ;;
    badgid) echo 'badgid:x:abc:' ;;
    leadzero) echo 'leadzero:x:007:' ;;
    *) exit 2 ;;
esac
SCRIPT
chmod +x "$work/bin/getent"
PATH="$work/bin:$PATH"
export PATH

failures=0
fail() {
    echo "FAIL: $1" >&2
    failures=$((failures + 1))
}
# run_lib 'shell snippet': runs the snippet with the library sourced; returns its status.
run_lib() {
    # shellcheck source=deploy/serval-common.sh
    (. "$lib"; eval "$1") > "$work/out" 2>&1
}
# write_via_lib DEST MODE: pipes standard input through serval_atomic_write.
write_via_lib() {
    # shellcheck source=deploy/serval-common.sh
    (. "$lib"; serval_atomic_write "$1" "$2" root:root)
}
expect_ok() {
    run_lib "$2" || fail "$1 (expected success): $(cat "$work/out")"
}
expect_rejected() {
    if run_lib "$2"; then
        fail "$1 (expected rejection)"
    fi
}

# --- admin group validation ---
expect_ok 'sudo accepted' 'serval_validate_admin_group sudo; [ "$SERVAL_ADMIN_GID" = 27 ]'
expect_ok 'underscore and hyphen accepted' 'serval_validate_admin_group ops_team-1; [ "$SERVAL_ADMIN_GID" = 2001 ]'
for bad in '' Sudo 1abc 'a b' 'a;b' 'a$(id)' '../sudo' '-sudo' '_sudo' 'sud*' 'sudo/x' "$(printf 'a\nsudo')" \
    "$(printf 'x%.0s' $(seq 1 65))"; do
    WORK_BAD=$bad
    export WORK_BAD
    expect_rejected "malformed group '$bad'" 'serval_validate_admin_group "$WORK_BAD"'
done
expect_rejected 'unknown group' 'serval_validate_admin_group nosuchgroup'
expect_rejected 'GID 0' 'serval_validate_admin_group root'
expect_rejected 'non-numeric GID' 'serval_validate_admin_group badgid'
expect_rejected 'leading-zero GID' 'serval_validate_admin_group leadzero'

# --- symlink checks ---
ln -s "$work/etc" "$work/linkdir"
touch "$work/plain"
ln -s "$work/plain" "$work/linkfile"
expect_rejected 'symlinked directory' "serval_reject_symlinks '$work/linkdir'"
expect_rejected 'symlinked file' "serval_reject_symlinks '$work/plain' '$work/linkfile'"
expect_ok 'plain and missing paths' "serval_reject_symlinks '$work/plain' '$work/missing'"

# --- atomic write ---
printf 'content\n' | write_via_lib "$work/etc/file" 0640 || fail 'atomic write failed'
[ "$(cat "$work/etc/file")" = content ] || fail 'atomic write content'
[ "$(stat -c %a "$work/etc/file")" = 640 ] || fail 'atomic write mode'
[ "$(stat -c %U:%G "$work/etc/file")" = root:root ] || fail 'atomic write owner'
[ "$(find "$work/etc" -mindepth 1 | wc -l)" = 1 ] || fail 'temporary file left behind'
echo precious > "$work/plain"
if echo evil | write_via_lib "$work/linkfile" 0600 2> /dev/null; then
    fail 'atomic write followed a symlinked destination'
fi
[ "$(cat "$work/plain")" = precious ] || fail 'symlink target was modified'
if echo evil | write_via_lib "$work/linkdir/file2" 0600 2> /dev/null; then
    fail 'atomic write used a symlinked directory'
fi
[ ! -e "$work/etc/file2" ] || fail 'file created through symlinked directory'
if : | write_via_lib "$work/etc/empty" 0600 2> /dev/null; then
    fail 'empty content was written'
fi
[ ! -e "$work/etc/empty" ] || fail 'empty file exists'

# --- agent.json creation and rewrite ---
config=$work/etc/agent.json
expect_ok 'create agent.json' "serval_write_agent_config '$config' 987 987 sudo 27"
jq -e '.webUid == 987 and .webGid == 987 and .adminGroup == "sudo" and .adminGid == 27 and .idleMinutes == 15' \
    "$config" > /dev/null || fail 'created agent.json content'
[ "$(stat -c %a:%U "$config")" = 600:root ] || fail 'agent.json mode or owner'
expect_rejected 'agent.json with mismatched gid' "serval_write_agent_config '$work/etc/other.json' 987 987 sudo 28"
expect_rejected 'agent.json with bad uid' "serval_write_agent_config '$work/etc/other.json' '9 7' 987 sudo 27"
[ ! -e "$work/etc/other.json" ] || fail 'rejected agent.json was created'

echo '{"webUid":987,"webGid":987,"adminGroup":"sudo","adminGid":27,"idleMinutes":42}' > "$config"
expect_ok 'rewrite group' "serval_rewrite_agent_group '$config' wheel"
jq -e '.adminGroup == "wheel" and .adminGid == 10 and .idleMinutes == 42 and .webUid == 987 and .webGid == 987' \
    "$config" > /dev/null || fail 'rewrite must change the group and preserve idleMinutes'
[ "$(stat -c %a:%U "$config")" = 600:root ] || fail 'rewritten agent.json mode or owner'

before=$(cat "$config")
expect_rejected 'rewrite to unknown group' "serval_rewrite_agent_group '$config' nosuchgroup"
expect_rejected 'rewrite to GID 0' "serval_rewrite_agent_group '$config' root"
expect_rejected 'rewrite to malformed group' "serval_rewrite_agent_group '$config' 'a;b'"
[ "$(cat "$config")" = "$before" ] || fail 'invalid group changed agent.json'
echo 'not json' > "$work/etc/broken.json"
expect_rejected 'rewrite of invalid JSON' "serval_rewrite_agent_group '$work/etc/broken.json' sudo"
[ "$(cat "$work/etc/broken.json")" = 'not json' ] || fail 'invalid JSON file was changed'
echo '[1]' > "$work/etc/array.json"
expect_rejected 'rewrite of non-object JSON' "serval_rewrite_agent_group '$work/etc/array.json' sudo"
ln -s "$config" "$work/etc/agent-link.json"
expect_rejected 'rewrite through symlink' "serval_rewrite_agent_group '$work/etc/agent-link.json' sudo"
expect_rejected 'rewrite of missing file' "serval_rewrite_agent_group '$work/etc/none.json' sudo"

if [ "$failures" -ne 0 ]; then
    echo "$failures check(s) failed." >&2
    exit 1
fi
echo 'serval-common checks passed.'
