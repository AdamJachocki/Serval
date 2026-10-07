#!/usr/bin/env bash
set -euo pipefail

root=$(cd -- "$(dirname -- "$0")/../../.." && pwd)
fixture=$(mktemp -d)
trap 'rm -rf -- "$fixture"' EXIT
cat > "$fixture/getent" <<'SCRIPT'
#!/usr/bin/env bash
set -euo pipefail
case "$1" in
    passwd)
        printf 'serval-web:x:987:987::%s:%s\n' \
            "${TEST_WEB_HOME:-/nonexistent}" "${TEST_WEB_SHELL:-/usr/sbin/nologin}"
        ;;
    shadow)
        printf 'serval-web:%s:20000:0:99999:7:::\n' "${TEST_WEB_PASSWORD:-!}"
        ;;
    *) exit 1 ;;
esac
SCRIPT
cat > "$fixture/id" <<'SCRIPT'
#!/usr/bin/env bash
set -euo pipefail
case "$1" in
    -u) echo 987 ;;
    -g) echo 987 ;;
    -gn) echo serval-web ;;
    -G) echo "${TEST_WEB_GROUPS:-987}" ;;
    *) exit 1 ;;
esac
SCRIPT
chmod +x "$fixture/getent" "$fixture/id"

verify() {
    PATH="$fixture:$PATH" bash "$root/deploy/validate-web-identity.sh" >/dev/null 2>&1
}

verify
if TEST_WEB_SHELL=/bin/bash verify; then
    echo 'Login shell was accepted.' >&2
    exit 1
fi
if TEST_WEB_HOME=/home/serval-web verify; then
    echo 'Interactive home was accepted.' >&2
    exit 1
fi
if TEST_WEB_PASSWORD=hashed-password verify; then
    echo 'Unlocked password was accepted.' >&2
    exit 1
fi
if TEST_WEB_GROUPS='987 999' verify; then
    echo 'Supplementary group was accepted.' >&2
    exit 1
fi
echo 'Web service identity deployment checks passed.'
