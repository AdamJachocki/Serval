# AGENTS.md — Serval

## Product

Serval is a production-grade, security-first, open-source Linux administration application.
It is not a proof of concept. Treat every change as production code.

Initial scope: manage environment variables of system-level systemd services and restart services explicitly.

## Technology

- .NET 10
- ASP.NET Core Razor Pages
- SQLite for Serval metadata/audit data only
- Linux PAM for authentication
- systemd system services only in v1
- Modular monolith
- Primary distribution: self-contained `.deb` packages for supported Ubuntu LTS and Debian stable releases
- Architectures: x64 and ARM64

## Process boundaries

Serval is split into at least two runtime processes:

- `serval-web`: network-facing web/UI process. MUST NOT run as root.
- `serval-agent`: minimal privileged local component responsible for approved system operations.

Communication between them must be local-only, preferably over a Unix Domain Socket.

Never move privileged system operations into `serval-web` for convenience.

## systemd rules

- Discover system-level systemd services automatically.
- Do not support user services in v1.
- NEVER modify vendor-owned or user-owned unit files directly.
- Read existing `Environment=` and `EnvironmentFile=` sources.
- Changes made by Serval MUST be implemented using Serval-owned systemd drop-ins and Serval-owned environment files.
- Removing a Serval override must restore the underlying systemd configuration naturally.
- Saving environment changes and restarting a service are separate, explicit operations.
- Never allow Serval to manage its own privileged components through the normal UI.

## Protected services

Some critical system services are protected by default.

- Protected services must not be readable, modifiable, or restartable through normal Serval permissions.
- The initial protected-service set may be a curated built-in list/pattern set.
- A protected service can only be enabled explicitly by a root-level local CLI/configuration action.
- Web administrators MUST NOT be able to bypass the protected-service boundary.

## Authentication and authorization

Authentication answers "who are you?" and is provided by Linux PAM.

Authorization answers "what may you do?" and is controlled by Serval.

Authorization is resource-based, not role-based.

A principal may be a Linux user or Linux group.

Per-service permissions should initially include:

- `Service.View`
- `Environment.Reveal`
- `Environment.Edit`
- `Service.Restart`

Global administrative permissions may exist for Serval configuration and ACL management.

UI labels such as "Read only", "Operate", or "Full control" may be implemented as permission presets only. Do not encode them as fixed domain roles.

Example:
- Adam may have only `Service.View` for service A.
- Adam may have all service permissions for service B.

Protected-service policy always takes precedence over normal ACLs.

## Sensitive data

Treat every environment-variable value as sensitive, regardless of its name.

Environment values MUST NOT be written to:

- application logs
- audit logs
- SQLite
- telemetry
- exception messages

The UI must mask values by default and require an explicit reveal action.

Audit who performed an operation, which service and variable were affected, and the action type — never the old or new value.

## Command execution

Never construct shell commands from user-controlled input.

Do not use `/bin/sh -c` for privileged operations.

Prefer typed APIs or direct process invocation with fixed executables and separately supplied validated arguments.

Validate systemd unit identifiers and every filesystem path crossing the privileged boundary.

## Network exposure

Secure default:

- bind Serval to localhost only.

Supported remote access:

- SSH local port forwarding.

Recommended network/public exposure:

- reverse proxy such as Caddy, nginx, or Apache terminating HTTPS and forwarding to Serval on localhost.

Do not make public network exposure the installation default.

## Storage

SQLite may store:

- ACLs
- audit metadata
- Serval configuration metadata
- other non-secret application state

SQLite must not store environment-variable values or Linux passwords.

Serval-owned configuration belongs under `/etc/serval`.
Persistent application state belongs under `/var/lib/serval`.

## Architecture

Keep the codebase a modular monolith with explicit boundaries.

Suggested projects/modules:

- `Serval.Web`
- `Serval.Agent`
- `Serval.Application`
- `Serval.Domain`
- `Serval.Infrastructure`
- `Serval.Systemd`

Systemd integration must be behind an application-facing abstraction so additional service managers can be added later without rewriting the application layer.

Avoid microservices unless a future requirement demonstrates a concrete need.

## Quality gates

Every change must pass:

- formatting
- build with warnings treated as errors
- unit tests
- relevant integration tests
- security-sensitive negative-path tests

Privileged and security-sensitive behavior requires tests for both permitted and denied paths.

Critical systemd behavior must be tested against a real Linux/systemd environment in CI. Do not rely only on mocks.

## Mandatory post-change review

- After every change covered by `.agents/workflows/post-change-review.md`, run that workflow before considering the task complete.
- Every review iteration must use a newly created `serval-code-reviewer` agent with a fresh context and no inherited implementation conversation.
- A task subject to review is complete only when the latest independent reviewer returns `VERDICT: APPROVED` and every applicable mandatory quality gate has passed.
- Review findings and every relevant quality-gate result must be reported according to the workflow.

## Mandatory project skills

- Load `.agents/skills/serval-systemd/SKILL.md` for every task involving systemd, units, service discovery or lifecycle, environment sources, drop-ins, effective service configuration, or Serval's systemd integration.
- Load `.agents/skills/serval-privileged-operation/SKILL.md` for every task involving `Serval.Agent`, Web-to-Agent IPC or Unix Domain Sockets, PAM, protected services, privileged authorization/escalation, privileged systemd or filesystem mutations, or new root capabilities.
- Load both when a task crosses both scopes. Their constraints are mandatory and supplement the security invariants below.

## Security invariants

Agents MUST NOT weaken these invariants to simplify implementation:

1. The network-facing process never runs as root.
2. Serval never directly edits vendor/user-owned systemd unit files.
3. Environment-variable values are always treated as secrets.
4. Protected-service restrictions cannot be bypassed through the web UI.
5. Privileged commands are never assembled through shell-string concatenation.
6. Save and restart remain separate explicit actions.
7. Authorization is checked server-side for every protected operation.
8. New privileged capabilities must be minimal, explicit, auditable, and tested.
