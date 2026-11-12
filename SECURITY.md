# Security policy

Serval is designed as security-sensitive Linux administration software. We appreciate responsible, private reports that help protect future users.

## Supported versions

Serval has not published a release yet. The default branch receives security fixes during development, but the repository itself is not a production deployment.

Once releases exist, only the **latest released version** receives security fixes. Releases are Debian packages (`serval`) for Debian 13 and Ubuntu 22.04, 24.04 and 26.04 on `amd64` and `arm64`, distributed through the signed APT repository described in [docs/packaging-and-release.md](docs/packaging-and-release.md). Update with `apt-get update && apt-get upgrade`.

The package bundles the .NET runtime, so a runtime security fix reaches hosts only through a new Serval release. Dependency and SDK updates are tracked weekly, and a security-relevant update is released as a new patch version. The runtime version of an installation is shown by `dpkg -s serval` (`Serval-Dotnet-Runtime`).

## Reporting a vulnerability

Do not open a public issue, discussion, or pull request for a suspected vulnerability. Use GitHub's **Security** tab and select **Report a vulnerability** to submit a private report to the maintainers.

Include only the information needed to reproduce and assess the issue:

- affected revision or version;
- affected component and security boundary;
- prerequisites and reproducible steps;
- expected and observed behavior;
- likely impact;
- suggested mitigation, if known.

Never include real credentials, environment-variable values, personal data, or production system details. Use synthetic values and redact logs before attaching them.

Maintainers will acknowledge the report, assess severity and scope, coordinate a fix and tests, and agree on disclosure timing with the reporter. Response targets will be published once the project has a staffed release and security-response process.

## Security expectations

The authoritative development invariants are maintained in [`AGENTS.md`](AGENTS.md). In particular, reports involving privilege boundaries, protected services, command or path injection, authorization bypass, secret exposure, or unintended service lifecycle operations are in scope.

### Identity and privileged boundary

`Serval.Web` runs under the dedicated unprivileged `serval-web` account. Only
`Serval.Agent` runs as root. Its local Unix socket checks the connecting
process's kernel UID. Every privileged request is parsed as one of eleven typed
operations and checked again by the Agent; Web-supplied identity, role or
service authorization is never authority. The Agent does not expose command
execution, arbitrary filesystem access, bulk environment disclosure, or service
mutations through this protocol. Environment metadata requires `Environment.View`;
single-value disclosure requires both `Environment.View` and `Environment.Reveal`.
The Agent authorizes before acquisition, rechecks current identity, grants and
protection after acquisition, and records value-free audit metadata before release.
Ordinary service inspection never acquires environment values.

### Web listener

In `Production`, `Serval.Web` reads its listener only from `/etc/serval/web.json`
(root-owned, group `serval-web`, not writable by group or others, not a symbolic
link) and ignores environment variables, command-line URLs and application
settings. It listens on `127.0.0.1:9080` over HTTP by default. Plain HTTP on a
non-loopback address is refused at startup; a network listener needs a
certificate and key from `/etc/serval/tls/` and then sends HSTS. Startup
errors never include key material.

Each reveal makes a fresh supported-declarations read. Only the requested value
crosses IPC in a disposable payload with redacted ordinary serialization and
diagnostics. Owned buffers are cleared on disposal and transport exits. Web writes
the value only to an anti-forgery-protected, no-store POST response and disposes
the payload. Browser rendering uses text; manual hiding and navigation discard
client state. There is no hiding timer. Managed strings, serializer internals and
browser memory cannot guarantee complete erasure, and revocation cannot recall
values already received. No values, hashes, lengths or source paths enter audit.

The Agent authenticates Linux accounts with the `serval` PAM service and
rechecks account status, Linux identity and group membership for authenticated
requests. Session records exist only in Agent memory and disappear on restart.
The browser cookie contains an opaque credential, is `Secure`, `HttpOnly`,
`SameSite=Strict` and session-only. Client IP is not part of authentication or
authorization. Anti-forgery validation protects browser POST actions.

Only current members of the configured existing host-admin group can manage
Serval grants. The Agent owns the root-only SQLite grant and audit database.
It evaluates current group membership and grants against canonical systemd
service IDs, checks the complete alias set and protected-service policy, and
denies access when identity, policy or audit checks fail. Built-in protected
services remain hidden even from Serval administrators.

A compromised Web process can send allowed requests through the socket and
reuse credentials or live sessions it observes. UID checking and Agent-side
authorization limit its scope but cannot distinguish those requests from
legitimate requests by the same process. Native PAM/NSS calls have bounded
caller waits and worker concurrency; a stuck native call can retain a worker
until it returns, so repeated hangs can make authentication unavailable until
the calls finish or the Agent restarts.
