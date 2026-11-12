#!/usr/bin/env bash
# Builds the self-contained `serval` Debian package for one architecture.
#
#   packaging/build-deb.sh --version 1.2.3 --arch amd64 [--output artifacts/packages] [--keyring <public keyring>]
#
# --keyring ships the APT repository's public keyring in the package (/usr/share/serval/); postinst
# refreshes the host's keyring file with it, so key expiry extensions and rotation arrive through upgrades.
#
# Requires: dotnet SDK (10.x), dpkg-deb, jq, gzip, xz. Runs on any Linux; no root needed.
set -euo pipefail

usage() {
    echo 'Usage: packaging/build-deb.sh --version <version|vTAG> --arch <amd64|arm64> [--output <dir>] [--keyring <file>]' >&2
    exit 2
}

version='' arch='' output='' keyring=''
while (( $# > 0 )); do
    case $1 in
        --version) version=${2-}; shift 2 || usage ;;
        --arch) arch=${2-}; shift 2 || usage ;;
        --output) output=${2-}; shift 2 || usage ;;
        --keyring) keyring=${2-}; shift 2 || usage ;;
        *) usage ;;
    esac
done
[[ -n $version && -n $arch ]] || usage

version=${version#v}
# A pre-release suffix (1.2.3-rc.1) sorts before the release in Debian only as 1.2.3~rc.1.
version=${version/-/\~}
if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+(~[0-9A-Za-z.]+)?$ ]]; then
    echo "Unsupported version '$version'." >&2
    exit 2
fi
case $arch in
    amd64) runtime=linux-x64 ;;
    arm64) runtime=linux-arm64 ;;
    *) echo "Unsupported architecture '$arch'." >&2; exit 2 ;;
esac

repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
output=${output:-$repo/artifacts/packages}
mkdir -p -- "$output"
output=$(cd -- "$output" && pwd)
maintainer=${SERVAL_PACKAGE_MAINTAINER:-Adam Jachocki <ajachocki@gmail.com>}
build=$(mktemp -d)
trap 'rm -rf -- "$build"' EXIT
stage=$build/serval_${version}_$arch

echo "Publishing Agent and Web for $runtime ..."
for project in Agent Web; do
    lower=${project,,}
    dotnet publish "$repo/src/Serval.$project/Serval.$project.csproj" \
        --configuration Release --runtime "$runtime" --self-contained true \
        -p:DebugType=None -p:DebugSymbols=false \
        --output "$stage/opt/serval/$lower" --nologo --verbosity quiet
done

runtime_version=$(jq -r '.libraries | keys[] | select(startswith("runtimepack.Microsoft.NETCore.App.Runtime"))' \
    "$stage/opt/serval/web/Serval.Web.deps.json" | head -n 1 | sed 's|.*/||')
[[ $runtime_version =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]] || { echo 'Could not determine the bundled .NET runtime version.' >&2; exit 1; }

# Explicit modes: root-owned (dpkg-deb --root-owner-group), never writable by group or others.
find "$stage/opt" -type d -exec chmod 0755 {} +
# The SDK marks every published file executable; only the two entry points need it.
find "$stage/opt" -type f -exec chmod 0644 {} +
chmod 0755 "$stage/opt/serval/agent/Serval.Agent" "$stage/opt/serval/web/Serval.Web"

install -d -m 0755 "$stage/DEBIAN" "$stage/usr/lib/systemd/system" "$stage/etc/pam.d" \
    "$stage/usr/lib/serval" "$stage/usr/share/doc/serval" "$stage/usr/share/lintian/overrides"
install -m 0644 "$repo/deploy/systemd/serval-agent.service" "$repo/deploy/systemd/serval-web.service" \
    "$stage/usr/lib/systemd/system/"
install -m 0644 "$repo/deploy/pam.d/serval" "$stage/etc/pam.d/serval"
# Sourced, never executed: the shebang line is dropped from the installed copy.
sed '1{/^#!/d}' "$repo/deploy/serval-common.sh" > "$stage/usr/lib/serval/serval-common.sh"
chmod 0644 "$stage/usr/lib/serval/serval-common.sh"
install -m 0644 "$repo/packaging/debian/copyright" "$stage/usr/share/doc/serval/copyright"
install -m 0644 "$repo/packaging/debian/lintian-overrides" "$stage/usr/share/lintian/overrides/serval"
install -d -m 0755 "$stage/usr/sbin" "$stage/usr/share/man/man8"
if [[ -n $keyring ]]; then
    [[ -s $keyring ]] || { echo "No such keyring: $keyring" >&2; exit 2; }
    install -d -m 0755 "$stage/usr/share/serval"
    install -m 0644 "$keyring" "$stage/usr/share/serval/serval-archive-keyring.gpg"
fi
install -m 0755 "$repo/packaging/serval-setup" "$stage/usr/sbin/serval-setup"
gzip -9n < "$repo/packaging/serval-setup.8" > "$stage/usr/share/man/man8/serval-setup.8.gz"
chmod 0644 "$stage/usr/share/man/man8/serval-setup.8.gz"

date_epoch=${SOURCE_DATE_EPOCH:-$(date +%s)}
{
    printf 'serval (%s) stable; urgency=medium\n\n' "$version"
    printf '  * Release %s with bundled .NET runtime %s.\n\n' "$version" "$runtime_version"
    printf ' -- %s  %s\n' "$maintainer" "$(date -u -R -d "@$date_epoch")"
} | gzip -9n > "$stage/usr/share/doc/serval/changelog.gz"
chmod 0644 "$stage/usr/share/doc/serval/changelog.gz"

# Maintainer scripts: preinst embeds the shared library (the installed copy is not unpacked yet on a
# first installation). The library's shebang line is dropped.
install -m 0755 "$repo/packaging/debian/postinst" "$repo/packaging/debian/prerm" \
    "$repo/packaging/debian/postrm" "$repo/packaging/debian/config" "$stage/DEBIAN/"
install -m 0644 "$repo/packaging/debian/templates" "$stage/DEBIAN/templates"
printf '/etc/pam.d/serval\n' > "$stage/DEBIAN/conffiles"
chmod 0644 "$stage/DEBIAN/conffiles"
awk -v common="$repo/deploy/serval-common.sh" '
    $0 == "@SERVAL_COMMON@" {
        while ((getline line < common) > 0) { if (line !~ /^#!/) print line }
        next
    }
    { print }' "$repo/packaging/debian/preinst" > "$stage/DEBIAN/preinst"
chmod 0755 "$stage/DEBIAN/preinst"

installed_size=$(du -sk --exclude=DEBIAN "$stage" | cut -f1)
cat > "$stage/DEBIAN/control" <<EOF
Package: serval
Version: $version
Architecture: $arch
Maintainer: $maintainer
Installed-Size: $installed_size
Depends: adduser, debconf (>= 1.5.19) | cdebconf, init-system-helpers (>= 1.54~), systemd, libpam0g, libpam-runtime, openssl, jq, libc6, libgcc-s1, libstdc++6, zlib1g, libicu78 | libicu76 | libicu74 | libicu72 | libicu70, libssl3t64 | libssl3
Section: admin
Priority: optional
Homepage: https://github.com/AdamJachocki/Serval
Serval-Dotnet-Runtime: $runtime_version
Description: Security-first Linux administration web application
 Serval lets host administrators view and manage systemd services from a web
 interface. A minimal privileged agent performs system operations; the
 network-facing web application runs as an unprivileged account and
 authenticates users through PAM.
 .
 This package bundles the .NET runtime and needs no separate .NET installation.
EOF

package=$output/serval_${version}_$arch.deb
dpkg-deb --root-owner-group -Zxz --build "$stage" "$package" > /dev/null
echo "Built $package"
