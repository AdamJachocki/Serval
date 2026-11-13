# Identity and authorization deployment

Serval uses Linux system accounts for interactive login. The Web process runs
as `serval-web`; the root Agent owns authentication, sessions, grant decisions
and audit. The installation script and unit files in `deploy/` are the
repository's deployment configuration for this boundary.

## Install the package

The `serval` Debian package is the supported way to install Serval on Debian 13
and Ubuntu 22.04, 24.04 and 26.04 (`amd64` and `arm64`). It bundles the .NET
runtime, so the host needs no .NET installation. Download the `.deb` for the
host's architecture from the GitHub Release (verify it against the attached
SHA-256 checksum) and install it as root:

```bash
apt-get install ./serval_<version>_<arch>.deb
```

Once releases are published, the signed APT repository is the simpler route:
`curl -fsSL https://adamjachocki.github.io/Serval/install.sh | sudo sh` adds
the repository and installs the package (see
[packaging-and-release.md](packaging-and-release.md)); later updates arrive
with `apt-get upgrade`. `install.sh` runs without prompts, so the host-admin
group is `sudo` unless you preseed it first (below) or change it afterwards
with `serval-setup admin-group`. To set up the repository by hand instead, see
"Manual repository setup" in [packaging-and-release.md](packaging-and-release.md).

Installation asks one debconf question, the host-admin group (default `sudo`).
For unattended installs preseed it before installing:

```bash
echo 'serval serval/admin-group string sudo' | debconf-set-selections
DEBIAN_FRONTEND=noninteractive apt-get install ./serval_<version>_<arch>.deb
```

The group must already exist, match the name grammar in `Serval.Agent` and not
be root's group; otherwise installation fails and neither service is started.
The package creates the `serval-web` account, installs the PAM policy and both
units (under `/usr/lib/systemd/system`), writes `/etc/serval/agent.json`,
enables both units and starts the Agent and then the Web application. It
refuses to install over an unsafe pre-existing `serval-web` account or a
symbolic link at `/etc/serval`, `/var/lib/serval` or `/etc/pam.d/serval`.

After installation Web listens only on `http://127.0.0.1:9080`. Nothing is
reachable from the network until you decide so with `serval-setup`.

### Reach the UI through an SSH tunnel

The default listener is plain HTTP on loopback. From your workstation:

```bash
ssh -L 9080:127.0.0.1:9080 admin@server.example
```

then open `http://localhost:9080` in Chromium or Firefox, which treat
`localhost` as a secure context and therefore accept Serval's `Secure` session
cookie. Some Safari versions do not; use HTTPS (below) there.

### Expose Web on the network with TLS

`serval-setup` is a local, root-only command (see `serval-setup(8)`). A
non-loopback address always requires TLS; Web refuses to start otherwise.

```bash
serval-setup listen --address 0.0.0.0 --port 8443 --tls self-signed --hostname admin.example.test
serval-setup listen --address 192.0.2.10 --port 8443 --tls custom --certificate /root/serval.pem --key /root/serval.key
serval-setup listen --loopback
serval-setup status
```

- `--tls self-signed` generates an ECDSA P-256 certificate valid for 365 days
  for the comma-separated `--hostname` names (DNS names or IP addresses). The
  command prints the certificate's SHA-256 fingerprint. Compare it with what
  your browser shows before accepting the certificate, over a channel you
  trust. Run the command again to renew the certificate; there is no automatic
  renewal.
- `--tls custom` takes a PEM certificate chain and an unencrypted private key
  as regular files given by absolute paths without symbolic links. The
  certificate must not be expired and must match the key. Both are copied into
  `/etc/serval/tls/`; the key is readable only by root and the `serval-web`
  group.
- Ports are 1024 through 65535 because Web runs without privilege to bind lower
  ports. Put a firewall or reverse proxy in front of the port if you need 443.
- Web is restarted after each change. If it does not start (for example the
  port is in use), the previous files are restored, Web is restarted with them
  and the command exits non-zero.
- `serval-setup listen --loopback` returns to loopback HTTP and removes the TLS
  files.

### Change the host-admin group

```bash
serval-setup admin-group serval-admins
```

The group is validated like at installation, `agent.json` is rewritten
atomically (other fields such as `idleMinutes` are kept) and the Agent and Web
are restarted. Members of the previous group lose grant administration;
members of the new group gain it. Existing sessions end with the restart.
`dpkg-reconfigure serval` does the same through the same code path.

### Upgrade, remove and purge

`apt-get install ./serval_<new-version>_<arch>.deb` (or `apt-get upgrade` once
a repository is configured) stops Web and then the Agent, copies `policy.db`
and any SQLite companion files to a root-only
`/var/lib/serval/backups/<previous-version>-<UTC timestamp>/` (the five newest
are kept), unpacks the new version and starts the Agent and then Web. If the
backup fails, the upgrade is aborted before any binary changes. If the Agent
fails its startup checks, Web is not started and the backup stays available.
`agent.json`, `web.json` and the TLS files are never overwritten.

To roll back, stop both services, copy the files from the backup directory
over `/var/lib/serval`, keeping owner root and mode `0600`, and install the
previous version with `apt-get install serval=<version>`.

`apt-get remove serval` stops both services and removes the binaries and
units; installing again enables and starts them. `apt-get purge serval` also removes `/etc/serval`, the PAM
policy and Web's state directory, but keeps `/var/lib/serval` (grants, audit
history and backups), which remain root-only, and the `serval-web` account.
Audit retention is an operator decision, so delete those yourself when you no
longer need them.

A host previously installed with `deploy/install-identity.sh` can install the
package over it: an unchanged copy of the old units in `/etc/systemd/system` is
removed so the packaged units apply, an existing `agent.json` is kept and the
database is backed up first. A modified unit copy stops the installation; move
your changes into a drop-in under `/etc/systemd/system/<unit>.d/` and remove
the copy.

## Install from source

From the repository root on a supported Linux host, publish the Agent and Web
for that host's architecture, install their executable outputs under
`/opt/serval/agent` and `/opt/serval/web`, and make those trees root-owned and
not writable by `serval-web`. The unit files expect executables named
`Serval.Agent` and `Serval.Web` at those paths. Set up HTTPS termination and
the Web listener for the deployment before exposing the UI.

Run `bash deploy/install-identity.sh <existing-host-admin-group>` as root.
The selected group must already exist; a current member gets Serval grant
administration and implicit `Service.View`, `Environment.View` and
`Environment.Reveal` on non-protected services. It is a
host administration group, so grant this membership according to the host's
normal administrative policy. The script creates `serval-web` if absent,
rejects supplementary groups, installs the PAM and systemd files, and writes
`/etc/serval/agent.json` with the resolved Web UID/GID and admin group
name/GID. Review the file before starting services. Its optional `idleMinutes`
field defaults to 15 and accepts 1 through 120. Start `serval-agent.service`
before `serval-web.service`; the Web unit depends on the Agent.

| Path | Owner and access | Purpose |
| --- | --- | --- |
| `/etc/serval/agent.json` | root, `0600` | Fixed Agent identity and idle settings |
| `/etc/pam.d/serval` | root, `0644` | PAM `common-auth` and `common-account` policy on Debian/Ubuntu |
| `/run/serval` | root:`serval-web`, `0750` | Agent socket directory managed by systemd |
| `/run/serval/agent.sock` | Agent-created, accessible to Web | Local typed IPC |
| `/var/lib/serval/policy.db` | root, `0600` | SQLite grants, favorites and audit |
| `/var/lib/serval-web/keys` | `serval-web` within its `0700` state directory | ASP.NET Core anti-forgery keys |
| `/etc/serval/web.json` | root:`serval-web`, `0640` (must not be writable by group or others, not a symlink) | Optional Web listener: `{"address", "port", "tls": {"certificate", "key"} \| null}` |
| `/etc/serval/tls/` | root:`serval-web`, `0750` | TLS files; `cert.pem` root `0644`, `key.pem` root:`serval-web` `0640` (no access for others) |

Without `web.json`, Web listens on `127.0.0.1:9080` over HTTP. The reader
accepts only the fields shown, a literal IPv4 or IPv6 address, a port from 1024
through 65535, and TLS files located directly in `/etc/serval/tls/`. A
non-loopback address without `tls` makes Web refuse to start.

The Agent refuses startup on missing or unsafe configuration, PAM file,
runtime directory, policy location, Web identity or admin group. The Web account
must be named `serval-web`, use `/nonexistent` as its home and
`/usr/sbin/nologin` as its shell, have a locked password, and have no
supplementary groups. Keep `/etc/serval`,
`/var/lib/serval` and installed binaries out of Web write access. PAM account
restrictions on this host apply to Serval login and subsequent session checks.

## Sessions, grants and operations

The browser receives a session-only, Secure, HttpOnly, SameSite=Strict cookie.
It does not contain Linux claims or grant decisions. Agent sessions expire
after inactivity, are revoked on logout, and disappear when Agent restarts;
users then log in again. Changing client IP alone does not end a session.
Account disablement or Linux identity/group changes are checked again by the
Agent. Changes to grants take effect on the next authorized request.

Ordinary users can see only concrete services with a matching user or group
`Service.View` grant. The Agent resolves canonical service identity and checks
all aliases and protected-service rules before returning metadata. Current
host-admin group members can manage grants but cannot view protected services.
Environment metadata and deliberate single-variable disclosure use separate
Agent-authorized operations. The protocol exposes no service mutation.

| Action | Required grants on the canonical service |
| --- | --- |
| Inventory and details page | `Service.View` |
| Environment metadata | `Environment.View` |
| Reveal one current value | `Environment.View` and `Environment.Reveal` |

Grant each operation independently. `Environment.Reveal` does not create
`Environment.View`; neither implies `Service.View`. The details page requires
`Service.View` in addition to its environment permissions. Direct Agent
environment operations do not require `Service.View`. Current host administrators
receive all three, but protected services and their aliases remain denied.

The policy database stores no password or session token. Audit rows contain
bounded actor, operation, service, outcome, correlation and timestamp fields,
plus a validated optional variable name for disclosure decisions. Values never
enter the database. A disclosure audit records release intent, not confirmed delivery.
An unavailable database blocks access, including administrator operations;
there is no bypass mode. Keep secure, root-accessible backups of
`/var/lib/serval/policy.db`. For a consistent file-level backup or restore,
stop the Agent and Web first, then copy the database and any SQLite companion
files together; keep ownership root and mode `0600` on restore. A SQLite online
backup can instead be made with a suitable SQLite backup tool. Preserve a
backup before changing schema or binaries. Audit retention and deletion are
operator tasks: stop services, retain records according to the host's policy,
prune only the `audit` table with a SQLite tool, check database integrity and
restart services. Never delete grants as an audit-retention shortcut.

## Schema 2 upgrade and rollback

Deploy matching Agent and Web binaries together. Stop Web and Agent, take the
consistent root-only backup described above, then install the matching builds.
Agent startup upgrades schema 1 to 2 in one SQLite transaction: it rebuilds the
grant operation constraint, preserves grant IDs and all audit history, adds nullable
`audit.variable_name`, and updates `user_version` last. Existing grants gain no
new permissions. Fresh databases start at version 2; repeated initialization is
idempotent. Migration failure rolls back and prevents Agent startup.

To roll back, stop both services and restore the complete pre-upgrade database
backup with its original companion files and root ownership/mode, together with
matching old binaries. Old Agent binaries reject schema 2. Never change
`user_version` or delete new grants/audits to make a downgrade appear compatible.
Restart Agent before Web; users must log in again because Agent sessions are
in-memory only.

## Schema 3 upgrade and rollback

The same stop, backup and matching-binaries procedure applies. Agent startup
upgrades schema 2 (or 1) to 3 in one SQLite transaction that only adds the
`favorites` table; grants and audit history are untouched. Fresh databases start
at version 3. Each user is limited to 100 favorites, and favorites for services
that are unauthorized or gone are hidden but still count towards that limit.

To roll back, stop both services and restore the complete pre-upgrade database
backup with its original companion files and root ownership/mode, together with
matching old binaries. Old Agent binaries reject schema 3 as an unsupported
schema, and any favorites added after the upgrade are lost with the restored
backup. Never change `user_version` or drop the table to make a downgrade appear
compatible.

## Verification

Check `systemctl status serval-agent.service serval-web.service`, the installed
UID/GID and file modes, and the Agent socket under `/run/serval`. Test login
with a permitted Linux account, default denial for an ungranted service, a
current host-admin grant change, logout and re-login after Agent restart.
Failures are intentionally sanitized in Web and Agent logs; inspect PAM and
host system logs under the host's access policy when diagnosing account
configuration. The repository's disposable real-systemd tests run through
`tests/Serval.Systemd.Tests/Fixtures/run-enumeration-tests.sh` with
`SERVAL_AGENT_TEST_EXECUTABLE` set to the published Linux Agent test runner.
