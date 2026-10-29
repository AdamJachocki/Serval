# Identity and authorization deployment

Serval uses Linux system accounts for interactive login. The Web process runs
as `serval-web`; the root Agent owns authentication, sessions, grant decisions
and audit. The installation script and unit files in `deploy/` are the
repository's deployment configuration for this boundary.

## Install and configure

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
| `/var/lib/serval/policy.db` | root, `0600` | SQLite grants and audit |
| `/var/lib/serval-web/keys` | `serval-web` within its `0700` state directory | ASP.NET Core anti-forgery keys |

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
