#!/usr/bin/env bash
# Tests packaging/build-apt-repo.sh: APT accepts the signed repository, installs an earlier version
# from it, and rejects modified, unsigned and wrongly signed metadata. Runs as root in a disposable
# Debian or Ubuntu container (see run-apt-repository-test.sh); no systemd is needed.
#   PACKAGES=/pkg REPO=/repo bash test-apt-repository.sh
# shellcheck disable=SC2015
set -uo pipefail

PACKAGES=${PACKAGES:?PACKAGES is required (directory with at least two serval versions)}
REPO=${REPO:-/repo}
export DEBIAN_FRONTEND=noninteractive
failures=0
pass() { printf 'ok   %s\n' "$1"; }
fail() { printf 'FAIL %s\n' "$1" >&2; failures=$((failures + 1)); }
assert() { local description=$1; shift; if "$@" > /dev/null 2>&1; then pass "$description"; else fail "$description"; fi; }
equals() { if [[ $2 == "$3" ]]; then pass "$1"; else fail "$1 (expected '$3', got '$2')"; fi; }

apt-get update -qq > /dev/null 2>&1
apt-get install -y -qq apt-utils gnupg gpgv ca-certificates xz-utils gzip > /dev/null 2>&1 ||
    { echo 'Could not install the test tools.' >&2; exit 1; }

work=$(mktemp -d)
chmod 0755 "$work"
export GNUPGHOME=$work/gnupg
mkdir -m 0700 "$GNUPGHOME"
new_key() { # name -> fingerprint
    gpg --batch --passphrase '' --quick-generate-key "$1 <$1@example.invalid>" rsa2048 sign never > /dev/null 2>&1
    gpg --batch --list-keys --with-colons "$1@example.invalid" | awk -F: '/^fpr/ { print $10; exit }'
}
key=$(new_key release)
other_key=$(new_key other)
[[ -n $key && -n $other_key ]] || { echo 'Could not create the throwaway keys.' >&2; exit 1; }

build() { bash "$REPO/packaging/build-apt-repo.sh" --packages "$PACKAGES" --output "$1" --key "$2" > "$work/build.log" 2>&1; }
build "$work/site" "$key"
assert 'the repository builds and verifies against its own key' test -s "$work/site/apt/dists/stable/InRelease"
for file in Release InRelease Release.gpg main/binary-amd64/Packages.xz main/binary-arm64/Packages.gz; do
    assert "dists/stable/$file exists" test -s "$work/site/apt/dists/stable/$file"
done
assert 'the published keyring is binary (dearmored)' bash -c "! head -c 20 '$work/site/serval-archive-keyring.gpg' | grep -q 'BEGIN PGP'"
assert 'the pool holds every supplied package' test "$(find "$work/site/apt/pool" -name '*.deb' | wc -l)" -eq "$(find "$PACKAGES" -name '*.deb' | wc -l)"

install -m 0644 "$work/site/serval-archive-keyring.gpg" /usr/share/keyrings/serval-test-keyring.gpg
use_site() { # directory containing apt/
    cat > /etc/apt/sources.list.d/serval-test.sources << EOF
Types: deb
URIs: file://$1/apt
Suites: stable
Components: main
Signed-By: /usr/share/keyrings/serval-test-keyring.gpg
EOF
    rm -rf /var/lib/apt/lists/*
}
update_log=$work/update.log
update() { apt-get update > "$update_log" 2>&1; }

arch=$(dpkg --print-architecture)
versions=$(for deb in "$PACKAGES"/*_"$arch".deb; do dpkg-deb -f "$deb" Version; done | sort -V)
oldest=$(printf '%s\n' "$versions" | head -1)
newest=$(printf '%s\n' "$versions" | tail -1)
[[ $oldest != "$newest" ]] || { echo "PACKAGES needs two versions for $arch." >&2; exit 1; }

use_site "$work/site"
assert 'apt-get update accepts the signed repository' update
equals 'the candidate is the newest version' "$(apt-cache policy serval | sed -n 's/^ *Candidate: //p')" "$newest"
assert "an earlier version is listed ($oldest)" bash -c "apt-cache madison serval | grep -q '$oldest'"
assert "apt-get install serval=$oldest works" apt-get install -y -qq "serval=$oldest"
equals 'the earlier version is installed' "$(dpkg-query -W -f='${Version}' serval)" "$oldest"
assert 'apt-get install serval upgrades to the newest version' apt-get install -y -qq serval
equals 'the newest version is installed' "$(dpkg-query -W -f='${Version}' serval)" "$newest"

# Rejections: every tampered copy must fail apt-get update, for the right reason.
rejects() { # description directory pattern
    use_site "$2"
    if update; then
        fail "$1 (apt-get update succeeded)"
    elif grep -Eq "$3" "$update_log"; then
        pass "$1"
    else
        fail "$1 (rejected for another reason: $(grep -E '^(E|W):' "$update_log" | head -2 | tr '\n' ' '))"
    fi
}
copy_site() { rm -rf "${work:?}/$1"; mkdir "$work/$1"; cp -a "$work/site/apt" "$work/$1/apt"; }

copy_site modified-release
sed -i 's/^Description: .*/Description: tampered/' "$work/modified-release/apt/dists/stable/InRelease"
rejects 'a modified InRelease is rejected' "$work/modified-release" 'BADSIG|not signed|signatures were invalid|Clearsigned file|could not be verified'

copy_site modified-index
for index in "$work"/modified-index/apt/dists/stable/main/binary-"$arch"/Packages*; do printf 'x' >> "$index"; done
rejects 'modified package indexes are rejected' "$work/modified-index" 'Hash Sum mismatch|Some index files failed'

copy_site unsigned
rm -f "$work/unsigned/apt/dists/stable/InRelease" "$work/unsigned/apt/dists/stable/Release.gpg"
rejects 'an unsigned repository is rejected' "$work/unsigned" 'not signed'

build "$work/other-site" "$other_key"
rejects 'a repository signed with another key is rejected' "$work/other-site" 'NO_PUBKEY|not signed|public key is not available|signatures were invalid|could not be verified'

# Remove the installed package first, otherwise apt has nothing to download.
dpkg --purge --force-all serval > /dev/null 2>&1
copy_site modified-package
deb=$(find "$work/modified-package/apt/pool" -name "*_${arch}.deb" | head -1)
printf 'x' >> "$deb"
use_site "$work/modified-package"
update > /dev/null 2>&1
name=$(basename -- "$deb")
if apt-get install -y -qq "serval=$(dpkg-deb -f "$PACKAGES/$name" Version)" > "$work/modified-package.log" 2>&1; then
    fail 'a modified .deb is rejected'
elif grep -Eq 'Hash Sum mismatch|File has unexpected size|Some files failed to download|Failed to fetch' "$work/modified-package.log"; then
    pass 'a modified .deb is rejected'
else
    fail "a modified .deb is rejected for another reason: $(tail -3 "$work/modified-package.log" | tr '\n' ' ')"
fi

# A published keyring may hold several keys (rollover): signatures by either key verify against it.
gpg --batch --export "$key" "$other_key" > "$work/multi-keyring.gpg"
for signer in "$key" "$other_key"; do
    bash "$REPO/packaging/build-apt-repo.sh" --packages "$PACKAGES" --output "$work/multi-site" --key "$signer" --keyring "$work/multi-keyring.gpg" > "$work/multi.log" 2>&1
    assert "a repository signed by ${signer: -8} publishes the supplied multi-key keyring" cmp -s "$work/multi-keyring.gpg" "$work/multi-site/serval-archive-keyring.gpg"
    install -m 0644 "$work/multi-site/serval-archive-keyring.gpg" /usr/share/keyrings/serval-test-keyring.gpg
    use_site "$work/multi-site"
    assert "apt accepts the multi-key keyring with a repository signed by ${signer: -8}" update
done
gpg --batch --export "$other_key" > "$work/wrong-keyring.gpg"
if bash "$REPO/packaging/build-apt-repo.sh" --packages "$PACKAGES" --output "$work/wrong-site" --key "$key" --keyring "$work/wrong-keyring.gpg" > "$work/wrong.log" 2>&1; then
    fail 'a keyring that does not contain the signing key is refused'
else
    pass 'a keyring that does not contain the signing key is refused'
fi

# A passphrase-protected signing key signs only with the passphrase file.
protected_home=$work/gnupg-protected
mkdir -m 0700 "$protected_home"
printf 'Synthetic-Passphrase' > "$work/passphrase"
GNUPGHOME=$protected_home gpg --batch --pinentry-mode loopback --passphrase-file "$work/passphrase"     --quick-generate-key 'protected <protected@example.invalid>' rsa2048 sign never > /dev/null 2>&1
protected_key=$(GNUPGHOME=$protected_home gpg --batch --list-keys --with-colons protected@example.invalid | awk -F: '/^fpr/ { print $10; exit }')
if GNUPGHOME=$protected_home bash "$REPO/packaging/build-apt-repo.sh" --packages "$PACKAGES" --output "$work/protected-site" --key "$protected_key" > "$work/protected.log" 2>&1; then
    fail 'a protected key cannot sign without its passphrase'
else
    pass 'a protected key cannot sign without its passphrase'
fi
if GNUPGHOME=$protected_home SERVAL_GPG_PASSPHRASE_FILE=$work/passphrase bash "$REPO/packaging/build-apt-repo.sh" --packages "$PACKAGES" --output "$work/protected-site" --key "$protected_key" > "$work/protected.log" 2>&1; then
    pass 'a protected key signs with the passphrase file'
else
    fail 'a protected key signs with the passphrase file'
    cat "$work/protected.log" >&2
fi
refute_leak() { ! grep -q 'Synthetic-Passphrase' "$work/protected.log"; }
assert 'the passphrase does not appear in the build output' refute_leak

rm -rf -- "$work"
if (( failures > 0 )); then
    echo "$failures check(s) failed." >&2
    exit 1
fi
echo 'All repository checks passed.'
