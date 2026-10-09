# Security policy

Serval is designed as security-sensitive Linux administration software. We appreciate responsible, private reports that help protect future users.

## Supported versions

Serval has no supported release yet. The default branch receives security fixes during development, but the bootstrap is not suitable for production deployment.

Once releases are available, this section will identify the versions receiving security updates.

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
process's kernel UID. Every privileged request is parsed as one of seven typed
operations and checked again by the Agent; Web-supplied identity, role or
service authorization is never authority. The Agent does not expose command
execution, arbitrary filesystem access, environment values, or service
mutations through this protocol.

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
