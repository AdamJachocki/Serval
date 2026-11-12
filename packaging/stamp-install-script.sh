#!/usr/bin/env bash
# Writes the publishable install.sh: packaging/install.sh with the repository address and the SHA-256
# of the repository keyring filled in.
#
#   packaging/stamp-install-script.sh --base-url https://example.invalid/serval \
#       --keyring <serval-archive-keyring.gpg> --output <install.sh> [--allow-http]
#
# The base URL must be https; --allow-http exists for the test harness that serves a local repository.
set -euo pipefail

usage() {
    echo 'Usage: packaging/stamp-install-script.sh --base-url <url> --keyring <file> --output <file> [--allow-http]' >&2
    exit 2
}

base_url='' keyring='' output='' allow_http=0
while (( $# > 0 )); do
    case $1 in
        --base-url) base_url=${2-}; shift 2 || usage ;;
        --keyring) keyring=${2-}; shift 2 || usage ;;
        --output) output=${2-}; shift 2 || usage ;;
        --allow-http) allow_http=1; shift ;;
        *) usage ;;
    esac
done
[[ -n $base_url && -n $keyring && -n $output ]] || usage
[[ $base_url =~ ^https?://[A-Za-z0-9._~:/@-]+$ && $base_url != */ ]] ||
    { echo 'The base URL must be http(s) without a trailing slash and use only URL-safe characters.' >&2; exit 2; }
[[ $base_url == https://* || $allow_http == 1 ]] ||
    { echo 'The base URL must use https.' >&2; exit 2; }
[[ -s $keyring ]] || { echo "No such keyring: $keyring" >&2; exit 1; }

here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
hash=$(sha256sum -- "$keyring" | cut -d ' ' -f 1)
sed -e "s|@BASE_URL@|$base_url|" -e "s|@KEYRING_SHA256@|$hash|" "$here/install.sh" > "$output"
chmod 0644 "$output"
if grep -q '@[A-Z_]*@' "$output"; then
    echo 'Placeholders remain in the stamped script.' >&2
    exit 1
fi
