#!/bin/sh
# Serval installer: adds the signed Serval APT repository and installs the `serval` package.
#
#   curl -fsSL <base-url>/install.sh | sudo sh
#
# Supported: Debian 13 and Ubuntu 22.04, 24.04, 26.04 on amd64 and arm64.
#
# The release workflow stamps the two placeholders below (packaging/stamp-install-script.sh), so
# this file only runs once published. The whole body is one function called on the last line: a
# truncated download defines the function and runs nothing. The host, the architecture and the
# downloaded keyring (by its pinned SHA-256) are verified before the keyring or the APT source is
# written; the only earlier change is installing curl and ca-certificates when curl is missing.
set -eu

serval_install() {
    base_url='@BASE_URL@'
    keyring_sha256='@KEYRING_SHA256@'
    PATH=/usr/sbin:/usr/bin:/sbin:/bin
    export PATH
    LC_ALL=C
    export LC_ALL

    die() {
        printf 'serval install: %s\n' "$1" >&2
        exit 1
    }

    case $base_url in
        https://* | http://*) ;;
        *) die 'this script has not been prepared for publication (no repository address).' ;;
    esac
    case $keyring_sha256 in
        '' | *[!0-9a-f]*) die 'this script has not been prepared for publication (no keyring hash).' ;;
    esac
    [ "${#keyring_sha256}" -eq 64 ] || die 'this script has not been prepared for publication (no keyring hash).'

    [ "$(id -u)" = 0 ] || die 'run this script as root, for example: curl -fsSL <url> | sudo sh'

    # Read, never source, os-release.
    os_id='' os_version=''
    if [ -r /etc/os-release ]; then
        os_id=$(sed -n 's/^ID=//p' /etc/os-release | head -n 1 | tr -d '"')
        os_version=$(sed -n 's/^VERSION_ID=//p' /etc/os-release | head -n 1 | tr -d '"')
    fi
    case "$os_id $os_version" in
        'debian 13' | 'ubuntu 22.04' | 'ubuntu 24.04' | 'ubuntu 26.04') ;;
        *) die 'unsupported system. Serval supports Debian 13 and Ubuntu 22.04, 24.04 and 26.04.' ;;
    esac

    command -v dpkg > /dev/null 2>&1 || die 'dpkg was not found; this is not a Debian-based system.'
    architecture=$(dpkg --print-architecture)
    case $architecture in
        amd64 | arm64) ;;
        *) die "unsupported architecture $architecture. Serval supports amd64 and arm64." ;;
    esac

    case $base_url in
        https://*) protocol='=https' ;;
        *) protocol='=http' ;;
    esac

    # Never let apt or debconf read the script from a pipe.
    DEBIAN_FRONTEND=noninteractive
    export DEBIAN_FRONTEND
    if ! command -v curl > /dev/null 2>&1; then
        echo 'Installing curl and ca-certificates...'
        apt-get update -qq < /dev/null
        apt-get install -y -qq --no-install-recommends ca-certificates curl < /dev/null
    fi

    work=$(mktemp -d)
    trap 'rm -rf -- "$work"' EXIT
    curl --fail --silent --show-error --location --proto "$protocol" --proto-redir "$protocol" \
        --max-filesize 1048576 --output "$work/keyring.gpg" "$base_url/serval-archive-keyring.gpg" ||
        die 'could not download the repository key.'
    actual=$(sha256sum "$work/keyring.gpg" | cut -d ' ' -f 1)
    [ "$actual" = "$keyring_sha256" ] || die 'the repository key does not match the expected SHA-256; nothing was changed.'

    keyring=/usr/share/keyrings/serval-archive-keyring.gpg
    source_file=/etc/apt/sources.list.d/serval.sources
    install -d -m 0755 /usr/share/keyrings /etc/apt/sources.list.d
    install -m 0644 "$work/keyring.gpg" "$keyring.new"
    mv -f -- "$keyring.new" "$keyring"
    cat > "$source_file.new" << EOF
Types: deb
URIs: $base_url/apt
Suites: stable
Components: main
Signed-By: $keyring
EOF
    chmod 0644 "$source_file.new"
    mv -f -- "$source_file.new" "$source_file"

    echo 'Installing serval...'
    apt-get update < /dev/null
    apt-get install -y serval < /dev/null
    echo 'Serval is installed. Web listens only on http://127.0.0.1:9080.'
    echo 'From your workstation, reach it through an SSH tunnel:'
    echo '  ssh -L 9080:127.0.0.1:9080 <user>@<this-host>   then open http://localhost:9080'
    echo 'Use serval-setup(8) to expose Web with TLS or to change the host-admin group.'
}

serval_install "$@"
