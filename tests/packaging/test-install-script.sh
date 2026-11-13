#!/usr/bin/env bash
# Tests packaging/install.sh against a locally served, throwaway-key repository. Runs as root in a
# disposable Debian or Ubuntu container (see run-install-script-test.sh); no systemd is needed.
#   PACKAGES=/pkg REPO=/repo bash test-install-script.sh
# shellcheck disable=SC2015
set -uo pipefail

PACKAGES=${PACKAGES:?PACKAGES is required (directory with serval packages)}
REPO=${REPO:-/repo}
export DEBIAN_FRONTEND=noninteractive
failures=0
pass() { printf 'ok   %s\n' "$1"; }
fail() { printf 'FAIL %s\n' "$1" >&2; failures=$((failures + 1)); }
assert() { local description=$1; shift; if "$@" > /dev/null 2>&1; then pass "$description"; else fail "$description"; fi; }
refute() { local description=$1; shift; if "$@" > /dev/null 2>&1; then fail "$description"; else pass "$description"; fi; }
equals() { if [[ $2 == "$3" ]]; then pass "$1"; else fail "$1 (expected '$3', got '$2')"; fi; }

apt-get update -qq > /dev/null 2>&1
apt-get install -y -qq apt-utils gnupg gpgv xz-utils gzip python3 > /dev/null 2>&1 ||
    { echo 'Could not install the test tools.' >&2; exit 1; }
getent group sudo > /dev/null || groupadd sudo

work=$(mktemp -d)
chmod 0755 "$work"
export GNUPGHOME=$work/gnupg
mkdir -m 0700 "$GNUPGHOME"
gpg --batch --passphrase '' --quick-generate-key 'release <release@example.invalid>' rsa2048 sign never > /dev/null 2>&1
key=$(gpg --batch --list-keys --with-colons release@example.invalid | awk -F: '/^fpr/ { print $10; exit }')
[[ -n $key ]] || { echo 'Could not create the throwaway key.' >&2; exit 1; }
bash "$REPO/packaging/build-apt-repo.sh" --packages "$PACKAGES" --output "$work/site" --key "$key" > "$work/build.log" 2>&1 ||
    { cat "$work/build.log" >&2; exit 1; }
cp "$work/site/serval-archive-keyring.gpg" "$work/keyring.good"

port=18480
(cd "$work/site" && exec python3 -m http.server "$port" --bind 127.0.0.1 > "$work/http.log" 2>&1) &
server=$!
trap 'kill $server 2> /dev/null; rm -rf -- "$work"' EXIT
for _ in $(seq 1 30); do (exec 3<> "/dev/tcp/127.0.0.1/$port") 2> /dev/null && break; sleep 0.2; done

base=http://127.0.0.1:$port
bash "$REPO/packaging/stamp-install-script.sh" --base-url "$base" --keyring "$work/site/serval-archive-keyring.gpg" --output "$work/install.sh" --allow-http
chmod 0755 "$work"
keyring=/usr/share/keyrings/serval-archive-keyring.gpg
source_file=/etc/apt/sources.list.d/serval.sources
log=$work/run.log

untouched() { [[ ! -e $keyring && ! -e $source_file ]] && ! dpkg-query -W -f='${Status}' serval 2> /dev/null | grep -q 'ok installed'; }
# Runs a script and records its exit status and output in $log.
run() { sh "$@" > "$log" 2>&1; }
reason() { grep -q -- "$1" "$log"; }

# --- refusals leave the host untouched -------------------------------------------------------
cp /etc/os-release "$work/os-release.orig"
set_os_release() { rm -f /etc/os-release; printf 'ID=%s\nVERSION_ID="%s"\n' "$1" "$2" > /etc/os-release; }
restore_os_release() { rm -f /etc/os-release; cp "$work/os-release.orig" /etc/os-release; }

for system in 'debian 12' 'ubuntu 20.04' 'ubuntu 25.10' 'fedora 40' 'linuxmint 22'; do
    set_os_release "${system% *}" "${system#* }"
    if run "$work/install.sh"; then fail "unsupported system '$system' is refused"; else
        reason 'unsupported system' && untouched && pass "unsupported system '$system' is refused and nothing changes" ||
            fail "unsupported system '$system' is refused for the wrong reason or changed the host"
    fi
done
restore_os_release

mv /usr/bin/dpkg /usr/bin/dpkg.real
cat > /usr/bin/dpkg << 'EOF'
#!/bin/sh
if [ "${1-}" = --print-architecture ]; then echo riscv64; exit 0; fi
exec /usr/bin/dpkg.real "$@"
EOF
chmod 0755 /usr/bin/dpkg
if run "$work/install.sh"; then fail 'an unsupported architecture is refused'; else
    reason 'unsupported architecture riscv64' && untouched && pass 'an unsupported architecture is refused and nothing changes' ||
        fail 'an unsupported architecture is refused for the wrong reason or changed the host'
fi
mv -f /usr/bin/dpkg.real /usr/bin/dpkg

if runuser -u nobody -- sh "$work/install.sh" > "$log" 2>&1; then fail 'a non-root run is refused'; else
    reason 'as root' && untouched && pass 'a non-root run is refused and nothing changes' ||
        fail 'a non-root run is refused for the wrong reason or changed the host'
fi

bash "$REPO/packaging/stamp-install-script.sh" --base-url "$base" --keyring "$work/site/serval-archive-keyring.gpg" --output "$work/install-unstamped-check.sh" --allow-http
sed -i "s|^    base_url='.*'|    base_url='@BASE_URL@'|" "$work/install-unstamped-check.sh"
if run "$work/install-unstamped-check.sh"; then fail 'the unprepared template is refused'; else
    reason 'not been prepared' && untouched && pass 'the unprepared template is refused' || fail 'the unprepared template is refused for the wrong reason'
fi
if run "$REPO/packaging/install.sh"; then fail 'the repository template is refused'; else
    reason 'not been prepared' && untouched && pass 'the repository template is refused until stamped' || fail 'the repository template is refused for the wrong reason'
fi

# --- a substituted key is rejected -----------------------------------------------------------
printf 'tampered' >> "$work/site/serval-archive-keyring.gpg"
if run "$work/install.sh"; then fail 'a key with the wrong SHA-256 is refused'; else
    reason 'does not match the expected SHA-256' && untouched && pass 'a key with the wrong SHA-256 is refused and nothing changes' ||
        fail 'a key with the wrong SHA-256 is refused for the wrong reason or changed the host'
fi
cp "$work/keyring.good" "$work/site/serval-archive-keyring.gpg"

# --- a truncated script runs nothing ---------------------------------------------------------
size=$(wc -c < "$work/install.sh")
truncated_ok=1
# A cut that only drops the final newline leaves the complete script, so the last cut is size - 2.
for cut in $(seq 1 97 "$((size - 2))") "$((size - 2))" "$((size - 12))"; do
    head -c "$cut" "$work/install.sh" > "$work/truncated.sh"
    sh "$work/truncated.sh" > "$work/truncated.log" 2>&1 || true
    if ! untouched || grep -q 'Installing' "$work/truncated.log"; then truncated_ok=0; echo "truncated at $cut bytes ran something" >&2; fi
done
[[ $truncated_ok == 1 ]] && pass 'scripts truncated at many offsets run nothing' || fail 'a truncated script ran something'
assert 'the script ends with the single call of its function' bash -c "tail -n 1 '$work/install.sh' | grep -qx 'serval_install \"\$@\"'"

# --- a supported host installs (curl is absent in the minimal image) -------------------------
newest=$(for deb in "$PACKAGES"/*_"$(dpkg --print-architecture)".deb; do dpkg-deb -f "$deb" Version; done | sort -V | tail -1)
apt-get purge -y -qq curl > /dev/null 2>&1
assert 'curl is absent before the install' bash -c '! command -v curl'
if run "$work/install.sh"; then pass 'a supported host installs through install.sh'; else fail 'a supported host installs through install.sh'; tail -20 "$log" >&2; fi
equals 'the newest version is installed' "$(dpkg-query -W -f='${Version}' serval 2> /dev/null)" "$newest"
equals 'the keyring is the published one' "$(sha256sum < "$keyring" | cut -d ' ' -f 1)" "$(sha256sum < "$work/keyring.good" | cut -d ' ' -f 1)"
equals 'the keyring mode is 0644' "$(stat -c %a "$keyring")" 644
assert 'the source names the repository and the keyring' bash -c "grep -qx 'URIs: $base/apt' $source_file && grep -qx 'Signed-By: $keyring' $source_file"
equals 'the source has one stanza' "$(grep -c '^Types:' "$source_file")" 1

# --- repeating the run keeps one source ------------------------------------------------------
before=$(sha256sum < "$source_file")
if run "$work/install.sh"; then pass 'a repeated run succeeds'; else fail 'a repeated run succeeds'; tail -20 "$log" >&2; fi
equals 'a repeated run leaves one unchanged source' "$(sha256sum < "$source_file")" "$before"
equals 'only one Serval source file exists' "$(grep -l 'serval' /etc/apt/sources.list.d/* /etc/apt/sources.list 2> /dev/null | wc -l)" 1

if (( failures > 0 )); then
    echo "$failures check(s) failed." >&2
    exit 1
fi
echo 'All install script checks passed.'
