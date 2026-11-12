#!/usr/bin/env bash
# End-to-end check of the published-style flow, as root in a systemd test container:
#   PACKAGE_TEST_SCRIPT=e2e-test.sh bash run-package-tests.sh <image> <new.deb> <old.deb>
#
# install.sh from a locally served signed repository (older version only) -> login over loopback
# HTTP with PAM users -> serval-setup listen with a self-signed certificate -> HTTPS login -> the
# newer version is published and installed with apt-get upgrade -> purge.
# shellcheck disable=SC2015
set -uo pipefail

NEW_DEB=${NEW_DEB:?NEW_DEB is required}
OLD_DEB=${OLD_DEB:?OLD_DEB is required}
REPO=${REPO:-/repo}
export DEBIAN_FRONTEND=noninteractive
failures=0
pass() { printf 'ok   %s\n' "$1"; }
fail() { printf 'FAIL %s\n' "$1" >&2; failures=$((failures + 1)); }
assert() { local description=$1; shift; if "$@" > /dev/null 2>&1; then pass "$description"; else fail "$description"; fi; }
refute() { local description=$1; shift; if "$@" > /dev/null 2>&1; then fail "$description"; else pass "$description"; fi; }
equals() { if [[ $2 == "$3" ]]; then pass "$1"; else fail "$1 (expected '$3', got '$2')"; fi; }
active() { [[ $(systemctl is-active "$1" 2> /dev/null) == active ]]; }
wait_active() { for _ in $(seq 1 40); do active "$1" && return 0; sleep 0.5; done; return 1; }

apt-get update -qq > /dev/null 2>&1
apt-get install -y -qq apt-utils gnupg gpgv xz-utils gzip python3 curl iproute2 openssl ca-certificates > /dev/null 2>&1 ||
    { echo 'Could not install the test tools.' >&2; exit 1; }
getent group sudo > /dev/null || groupadd sudo
useradd -m alice && echo 'alice:Synthetic-Pw1' | chpasswd && usermod -aG sudo alice
useradd -m bob && echo 'bob:Synthetic-Pw2' | chpasswd

old_version=$(dpkg-deb -f "$OLD_DEB" Version)
new_version=$(dpkg-deb -f "$NEW_DEB" Version)

work=$(mktemp -d)
chmod 0755 "$work"
export GNUPGHOME=$work/gnupg
mkdir -m 0700 "$GNUPGHOME"
gpg --batch --passphrase '' --quick-generate-key 'release <release@example.invalid>' rsa2048 sign never > /dev/null 2>&1
key=$(gpg --batch --list-keys --with-colons release@example.invalid | awk -F: '/^fpr/ { print $10; exit }')
port=18480
base=http://127.0.0.1:$port
mkdir -p "$work/site" "$work/v1" "$work/v2"
cp "$OLD_DEB" "$work/v1/"
cp "$OLD_DEB" "$NEW_DEB" "$work/v2/"
publish() { bash "$REPO/packaging/assemble-site.sh" --packages "$1" --key "$key" --base-url "$base" --allow-http --output "$work/site" > "$work/publish.log" 2>&1; }
publish "$work/v1" || { cat "$work/publish.log" >&2; exit 1; }
(cd "$work/site" && exec python3 -m http.server "$port" --bind 127.0.0.1 > "$work/http.log" 2>&1) &
server=$!
trap 'kill $server 2> /dev/null; rm -rf -- "$work"' EXIT
for _ in $(seq 1 30); do (exec 3<> "/dev/tcp/127.0.0.1/$port") 2> /dev/null && break; sleep 0.2; done

# Logs in through Web with PAM credentials; prints the session token (empty on failure).
web_session() { # base-url user password
    local jar body token
    jar=$(mktemp)
    body=$(curl -ks -c "$jar" -b "$jar" "$1/Login")
    token=$(sed -n 's/.*<input[^>]*name="__RequestVerificationToken"[^>]*value="\([^"]*\)".*/\1/p' <<< "$body" | head -1)
    curl -ks -i -c "$jar" -b "$jar" --data-urlencode "Username=$2" --data-urlencode "Password=$3" \
        --data-urlencode "__RequestVerificationToken=$token" "$1/Login" |
        sed -n 's/^[Ss]et-[Cc]ookie: __Host-ServalSession=\([^;]*\).*/\1/p' | head -1
    rm -f "$jar"
}
grants_status() { # base-url token
    curl -ks -o /dev/null -w '%{http_code}' -H "Cookie: __Host-ServalSession=$2" "$1/Grants"
}
listeners() { ss -ltnpH 2> /dev/null | grep '"Serval.Web"' | awk '{print $4}' | sort | tr '\n' ' '; }

echo "== 1. install.sh from the signed repository ($old_version)"
if curl -fsSL "$base/install.sh" | sh > "$work/install.log" 2>&1; then pass 'curl | sh installs serval'; else fail 'curl | sh installs serval'; tail -20 "$work/install.log" >&2; fi
equals 'the repository version is installed' "$(dpkg-query -W -f='${Version}' serval 2> /dev/null)" "$old_version"
assert 'Agent is active' wait_active serval-agent
assert 'Web is active' wait_active serval-web
equals 'Web listens on loopback only' "$(listeners)" '127.0.0.1:9080 '

echo '== 2. loopback HTTP login (the SSH-tunnel case)'
alice=$(web_session http://127.0.0.1:9080 alice Synthetic-Pw1)
bob=$(web_session http://127.0.0.1:9080 bob Synthetic-Pw2)
assert 'alice and bob log in with their PAM passwords' test -n "$alice" -a -n "$bob"
equals 'a wrong password does not log in' "$(web_session http://127.0.0.1:9080 alice wrong-password)" ''
equals 'alice (host-admin group) administers grants' "$(grants_status http://127.0.0.1:9080 "$alice")" 200
equals 'bob (not in the group) is denied' "$(grants_status http://127.0.0.1:9080 "$bob")" 403

echo '== 3. serval-setup listen with a self-signed certificate'
if serval-setup listen --address 0.0.0.0 --port 8443 --tls self-signed --hostname localhost > "$work/setup.log" 2>&1; then pass 'serval-setup listen succeeds'; else fail 'serval-setup listen succeeds'; cat "$work/setup.log" >&2; fi
assert 'Web is active after the change' wait_active serval-web
equals 'Web listens on the TLS port only' "$(listeners)" '0.0.0.0:8443 '
printed=$(sed -n 's/^ *SHA256://p' "$work/setup.log" | head -1)
served=$(openssl s_client -connect 127.0.0.1:8443 -servername localhost < /dev/null 2> /dev/null | openssl x509 -noout -fingerprint -sha256 | sed 's/^[^=]*=//')
equals 'the printed fingerprint is the served certificate' "$printed" "$served"
alice=$(web_session https://localhost:8443 alice Synthetic-Pw1)
assert 'alice logs in over HTTPS' test -n "$alice"
equals 'alice administers grants over HTTPS' "$(grants_status https://localhost:8443 "$alice")" 200
equals 'bob is denied over HTTPS' "$(grants_status https://localhost:8443 "$(web_session https://localhost:8443 bob Synthetic-Pw2)")" 403
refute 'the old loopback HTTP port no longer answers' curl -s -o /dev/null --max-time 3 http://127.0.0.1:9080/Login
web_json=$(cat /etc/serval/web.json)

echo "== 4. upgrade to the newer version ($new_version)"
publish "$work/v2" || { fail 'the newer version is published'; cat "$work/publish.log" >&2; }
if apt-get update > "$work/upgrade.log" 2>&1 && apt-get upgrade -y >> "$work/upgrade.log" 2>&1; then pass 'apt-get upgrade succeeds'; else fail 'apt-get upgrade succeeds'; tail -20 "$work/upgrade.log" >&2; fi
equals 'the newer version is installed' "$(dpkg-query -W -f='${Version}' serval)" "$new_version"
assert 'a root-only backup of the database exists' bash -c "ls /var/lib/serval/backups/$old_version-*/policy.db"
assert 'Agent is active after the upgrade' wait_active serval-agent
assert 'Web is active after the upgrade' wait_active serval-web
equals 'web.json survived the upgrade' "$(cat /etc/serval/web.json)" "$web_json"
equals 'Web still listens on the TLS port only' "$(listeners)" '0.0.0.0:8443 '
alice=$(web_session https://localhost:8443 alice Synthetic-Pw1)
equals 'alice administers grants after the upgrade' "$(grants_status https://localhost:8443 "$alice")" 200

echo '== 5. purge'
if apt-get purge -y serval > "$work/purge.log" 2>&1; then pass 'apt-get purge succeeds'; else fail 'apt-get purge succeeds'; tail -10 "$work/purge.log" >&2; fi
refute 'the services are stopped' bash -c 'systemctl is-active --quiet serval-agent || systemctl is-active --quiet serval-web'
refute 'nothing listens on the TLS port' bash -c 'ss -ltnH "sport = :8443" | grep -q .'
equals "binaries, configuration and TLS files are gone" "$(find /opt/serval /etc/serval /etc/pam.d/serval /usr/sbin/serval-setup -maxdepth 0 2> /dev/null | wc -l)" 0
assert 'grants, audit history and backups are kept' test -f /var/lib/serval/policy.db -a -d /var/lib/serval/backups
assert 'the Web account is kept' getent passwd serval-web

if (( failures > 0 )); then
    echo "$failures check(s) failed." >&2
    exit 1
fi
echo 'All end-to-end checks passed.'
