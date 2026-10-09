## Why

Serval has internal system-service readers but no multi-user login, verifiable Web-to-Agent identity delegation, or Agent-side resource authorization. Exposing privileged capabilities before those boundaries exist would let an unprivileged Web process make claims about users and permissions that the Agent cannot trust.

## What Changes

- Authenticate users with their Linux system accounts through PAM, including account-validity checks, without creating a separate Serval password database or retaining credentials after authentication.
- Establish Agent-verifiable, revocable user sessions for Web requests. Browser sessions are non-persistent, support immediate logout, and expire after 15 minutes of inactivity by default; a host administrator can configure that interval. A client IP change may be an anomaly signal but is not the session's proof of identity.
- Add local-only, authenticated, narrow, typed IPC between unprivileged `Serval.Web` and privileged `Serval.Agent`. The Agent verifies both the Web peer and the delegated user session; caller-supplied usernames, roles, groups, and authorization flags are not authority.
- Manage Serval permissions by operation and canonical system-service identity, assigning them to Linux users or groups. A host-administrator group selected during installation provides initial Serval administration without a separate bootstrap account. The Agent resolves current identity and group membership, checks current permissions for each request, and applies protected-service policy before returning data or performing an operation. Authorization and policy failures fail closed.
- Make the existing read-only system-service inventory and inspection available through this boundary as the first service capabilities, with `Service.View` enforcement and value-free audit metadata. Serval's own privileged units remain excluded; protected-service rules take precedence over normal permissions.
- Document the trust limit: a compromised Web process can misuse sessions or credentials available to it, although it cannot create a valid Agent identity or grant itself a new permission by asserting request fields.

This change does not expose service environment values, add lifecycle or configuration mutations, manage user-level systemd services, generate polkit rules, or add a remote Agent endpoint. It does not make browser sessions persistent or treat IP address as a hard authorization factor.

## Capabilities

### New Capabilities

- `identity/system-account-sessions`: PAM-backed system-account login, Agent-verifiable sessions, logout, expiry, and revocation.
- `authorization/system-service-access`: Linux user/group subjects, Serval operation-and-service permissions, host-administrator bootstrap, and protected-target precedence.
- `ipc/agent-operations`: authenticated local Web-to-Agent transport, delegated-session verification, bounded typed requests, and authorized service inventory/inspection responses.

### Modified Capabilities

None. The current accepted `environment-file-parsing` specification is unaffected.

## Impact

- `Serval.Web` gains login/session handling and an unprivileged IPC client; `Serval.Agent` gains PAM-backed authentication, session and authorization enforcement, narrow IPC hosting, and read-only service operation dispatch.
- Application-facing identity, permission, and service-operation contracts will be needed. The existing `Serval.Systemd` inventory/inspection implementation remains the systemd adapter; Agent policy governs its exposure.
- Installation must select and provision the Web IPC peer identity and host-administrator group. Policy storage, audit delivery, session state, socket ownership, and restart behavior require explicit design before implementation. SQLite may hold only non-secret application state.
- The trust-boundary and operational decisions will require updates to canonical documentation and permitted/denied, malformed-input, alias, protected-target, session-revocation, and real-systemd integration tests during implementation.
