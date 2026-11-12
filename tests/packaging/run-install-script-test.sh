#!/usr/bin/env bash
# Runs tests/packaging/test-install-script.sh in a disposable container.
#
#   tests/packaging/run-install-script-test.sh <base-image> <package-dir>
#   e.g. tests/packaging/run-install-script-test.sh ubuntu:24.04 artifacts/packages
#
# <package-dir> holds at least two versions of the serval package for the container's architecture.
set -euo pipefail

(( $# == 2 )) || { echo 'Usage: run-install-script-test.sh <base-image> <package-dir>' >&2; exit 2; }
base=$1 packages=$2
export MSYS_NO_PATHCONV=1
hostpath() { if command -v cygpath > /dev/null 2>&1; then cygpath -w -- "$1"; else printf '%s' "$1"; fi; }
here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd -- "$here/../.." && pwd)
packages=$(cd -- "$packages" && pwd)

docker run --rm \
    --volume "$(hostpath "$packages"):/pkg:ro" --volume "$(hostpath "$repo"):/repo:ro" \
    --env PACKAGES=/pkg --env REPO=/repo "$base" bash /repo/tests/packaging/test-install-script.sh
