#!/usr/bin/env bash
# Builds the signed APT repository for the `serval` package: suite stable, component main,
# architectures amd64 and arm64, from every .deb in a directory (all released versions).
#
#   packaging/build-apt-repo.sh --packages <dir> --output <dir> --key <key id or fingerprint> [--keyring <file>]
#
# --keyring publishes that public keyring (which may hold several keys, for a rollover) instead of
# exporting the signing key; the signatures must verify against it.
#
# Result:
#   <output>/apt/pool/main/s/serval/*.deb
#   <output>/apt/dists/stable/{Release,InRelease,Release.gpg,main/binary-<arch>/Packages*}
#   <output>/serval-archive-keyring.gpg   (the public key, dearmored; install.sh pins its SHA-256)
#
# The signing key is taken from the GnuPG home in $GNUPGHOME (default ~/.gnupg). If it has a
# passphrase, put it in a file and name that file in $SERVAL_GPG_PASSPHRASE_FILE. Requires: apt-ftparchive (apt-utils), gpg, gpgv, dpkg-deb, gzip, xz.
set -euo pipefail

usage() {
    echo 'Usage: packaging/build-apt-repo.sh --packages <dir> --output <dir> --key <key id or fingerprint> [--keyring <file>]' >&2
    exit 2
}

packages='' output='' key='' keyring=''
while (( $# > 0 )); do
    case $1 in
        --packages) packages=${2-}; shift 2 || usage ;;
        --output) output=${2-}; shift 2 || usage ;;
        --key) key=${2-}; shift 2 || usage ;;
        --keyring) keyring=${2-}; shift 2 || usage ;;
        *) usage ;;
    esac
done
[[ -n $packages && -n $output && -n $key ]] || usage
[[ -d $packages ]] || { echo "No such directory: $packages" >&2; exit 2; }
[[ $key =~ ^[0-9A-Fa-f]{8,40}$ ]] || { echo 'The key must be a hexadecimal key ID or fingerprint.' >&2; exit 2; }

shopt -s nullglob
debs=("$packages"/*.deb)
(( ${#debs[@]} > 0 )) || { echo "No .deb files in $packages" >&2; exit 1; }

architectures=(amd64 arm64)
for deb in "${debs[@]}"; do
    [[ $(dpkg-deb -f "$deb" Package) == serval ]] || { echo "Not a serval package: $deb" >&2; exit 1; }
    arch=$(dpkg-deb -f "$deb" Architecture)
    [[ " ${architectures[*]} " == *" $arch "* ]] || { echo "Unsupported architecture $arch in $deb" >&2; exit 1; }
done

mkdir -p -- "$output"
output=$(cd -- "$output" && pwd)
site=$output/apt
suite=$site/dists/stable
rm -rf -- "${site:?}"
mkdir -p -- "$site/pool/main/s/serval"
for deb in "${debs[@]}"; do
    install -m 0644 -- "$deb" "$site/pool/main/s/serval/"
done

(
    cd -- "$site"
    for arch in "${architectures[@]}"; do
        dir=dists/stable/main/binary-$arch
        mkdir -p -- "$dir"
        apt-ftparchive --arch "$arch" packages pool/main > "$dir/Packages"
        gzip -9nk -- "$dir/Packages"
        xz -9ek -- "$dir/Packages"
    done
    apt-ftparchive \
        -o APT::FTPArchive::Release::Origin=Serval \
        -o APT::FTPArchive::Release::Label=Serval \
        -o APT::FTPArchive::Release::Suite=stable \
        -o APT::FTPArchive::Release::Codename=stable \
        -o "APT::FTPArchive::Release::Architectures=${architectures[*]}" \
        -o APT::FTPArchive::Release::Components=main \
        -o "APT::FTPArchive::Release::Description=Serval Debian package repository" \
        release dists/stable > dists/stable/Release
)

gpg_sign=(gpg --batch --yes --pinentry-mode loopback --local-user "$key" --digest-algo SHA512)
# A passphrase-protected key: the passphrase is read from this file, never from the command line.
if [[ -n ${SERVAL_GPG_PASSPHRASE_FILE-} ]]; then
    [[ -r $SERVAL_GPG_PASSPHRASE_FILE ]] || { echo 'The passphrase file is not readable.' >&2; exit 2; }
    gpg_sign+=(--passphrase-file "$SERVAL_GPG_PASSPHRASE_FILE")
fi
"${gpg_sign[@]}" --clearsign --output "$suite/InRelease" "$suite/Release"
"${gpg_sign[@]}" --armor --detach-sign --output "$suite/Release.gpg" "$suite/Release"

if [[ -n $keyring ]]; then
    [[ -s $keyring ]] || { echo "No such keyring: $keyring" >&2; exit 2; }
    install -m 0644 -- "$keyring" "$output/serval-archive-keyring.gpg"
else
    gpg --batch --yes --export --output "$output/serval-archive-keyring.gpg" "$key"
fi
[[ -s $output/serval-archive-keyring.gpg ]] || { echo 'The public key could not be exported.' >&2; exit 1; }

# The published files must verify against the published key before anything is uploaded.
gpgv --keyring "$output/serval-archive-keyring.gpg" "$suite/InRelease" > /dev/null 2>&1 ||
    { echo 'InRelease does not verify against the exported key.' >&2; exit 1; }
gpgv --keyring "$output/serval-archive-keyring.gpg" "$suite/Release.gpg" "$suite/Release" > /dev/null 2>&1 ||
    { echo 'Release.gpg does not verify against the exported key.' >&2; exit 1; }

echo "Repository written to $site"
