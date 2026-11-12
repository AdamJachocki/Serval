#!/usr/bin/env bash
# Assembles the site published on GitHub Pages: the signed APT repository, the public keyring and the
# stamped install.sh.
#
#   packaging/assemble-site.sh --packages <dir> --key <key id or fingerprint> --base-url <url> --output <dir> [--keyring <file>] [--allow-http]
#
# The signing key comes from the GnuPG home in $GNUPGHOME. Requires what build-apt-repo.sh requires.
set -euo pipefail

usage() {
    echo 'Usage: packaging/assemble-site.sh --packages <dir> --key <key> --base-url <url> --output <dir> [--keyring <file>] [--allow-http]' >&2
    exit 2
}

packages='' key='' base_url='' output='' keyring='' allow_http=()
while (( $# > 0 )); do
    case $1 in
        --packages) packages=${2-}; shift 2 || usage ;;
        --key) key=${2-}; shift 2 || usage ;;
        --base-url) base_url=${2-}; shift 2 || usage ;;
        --output) output=${2-}; shift 2 || usage ;;
        --keyring) keyring=${2-}; shift 2 || usage ;;
        --allow-http) allow_http=(--allow-http); shift ;;
        *) usage ;;
    esac
done
[[ -n $packages && -n $key && -n $base_url && -n $output ]] || usage

here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
keyring_args=()
[[ -z $keyring ]] || keyring_args=(--keyring "$keyring")
bash "$here/build-apt-repo.sh" --packages "$packages" --output "$output" --key "$key" "${keyring_args[@]}"
bash "$here/stamp-install-script.sh" --base-url "$base_url" --keyring "$output/serval-archive-keyring.gpg" --output "$output/install.sh" "${allow_http[@]}"
: > "$output/.nojekyll"
cat > "$output/index.html" << EOF
<!doctype html>
<meta charset="utf-8">
<title>Serval packages</title>
<h1>Serval packages</h1>
<p>Install on Debian 13 or Ubuntu 22.04, 24.04, 26.04 as root:</p>
<pre>curl -fsSL $base_url/install.sh | sudo sh</pre>
EOF
echo "Keyring SHA-256: $(sha256sum -- "$output/serval-archive-keyring.gpg" | cut -d ' ' -f 1)"
