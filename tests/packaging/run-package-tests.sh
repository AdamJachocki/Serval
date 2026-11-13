#!/usr/bin/env bash
# Runs tests/packaging/package-tests.sh in a systemd container built from a minimal base image.
#
#   tests/packaging/run-package-tests.sh <base-image> <new.deb> <old.deb> [scenario...]
#   e.g. tests/packaging/run-package-tests.sh debian:13-slim artifacts/packages/serval_0.2.0_amd64.deb artifacts/packages/serval_0.1.0_amd64.deb
#
# PACKAGE_TEST_SCRIPT selects another script from this directory (tests/packaging/e2e-test.sh).
# The container is privileged and disposable. The packages and this repository are mounted read-only.
set -euo pipefail

(( $# >= 3 )) || { echo 'Usage: run-package-tests.sh <base-image> <new.deb> <old.deb> [scenario...]' >&2; exit 2; }
base=$1 new=$2 old=$3
shift 3
# Git Bash on Windows rewrites container paths; convert host paths explicitly and stop it guessing.
export MSYS_NO_PATHCONV=1
hostpath() { if command -v cygpath > /dev/null 2>&1; then cygpath -w -- "$1"; else printf '%s' "$1"; fi; }
here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd -- "$here/../.." && pwd)
tag=$(printf '%s' "$base" | tr -c 'A-Za-z0-9' '-')
image=serval-systemd-$tag
container=serval-pkg-test-$tag-$$
pkgdir=$(mktemp -d)
cp -- "$new" "$old" "$pkgdir/"
trap 'docker rm --force "$container" > /dev/null 2>&1 || true; rm -rf -- "$pkgdir"' EXIT

docker build --quiet --build-arg "BASE=$base" --file "$(hostpath "$here")/systemd.Dockerfile" --tag "$image" "$(hostpath "$here")" > /dev/null
docker run --detach --name "$container" --privileged --cgroupns=host --tmpfs /run:rw,exec,mode=755 \
    --tmpfs /run/lock --volume /sys/fs/cgroup:/sys/fs/cgroup:rw --volume "$(hostpath "$pkgdir"):/pkg:ro" \
    --volume "$(hostpath "$here"):/tests:ro" --volume "$(hostpath "$repo"):/repo:ro" "$image" > /dev/null

state=
for _ in $(seq 1 60); do
    state=$(docker exec "$container" systemctl is-system-running 2> /dev/null || true)
    [[ $state == running || $state == degraded ]] && break
    sleep 1
done
[[ $state == running || $state == degraded ]] || { echo "systemd did not boot in $base (state: $state)." >&2; exit 1; }

docker exec --env "NEW_DEB=/pkg/$(basename -- "$new")" --env "OLD_DEB=/pkg/$(basename -- "$old")" \
    "$container" bash "/tests/${PACKAGE_TEST_SCRIPT:-package-tests.sh}" "$@"
