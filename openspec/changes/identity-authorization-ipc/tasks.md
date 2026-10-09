## 1. Agent boundary and deployment prerequisites

- [x] 1.1 Define application-facing typed IPC request/result contracts for the seven allowed operations and verify contract tests reject unknown fields, versions, oversized frames, duplicate fields, invalid UTF-8 and unsupported operations.
- [x] 1.2 Host the Unix Domain Socket in `Serval.Agent` with fixed paths, kernel peer-UID verification, timeouts and concurrency limits; verify an allowed Web UID succeeds while a different UID, spoofed message identity and stalled client cannot dispatch an operation.
- [x] 1.3 Add Linux deployment configuration for dedicated Web/Agent identities, root-owned socket/policy locations, selected existing host-admin group, PAM service and configurable idle interval; verify valid startup and fail-closed behavior for missing, mismatched or unsafe configuration.

## 2. Authentication and sessions

- [x] 2.1 Implement Linux PAM authentication and account checks in Agent with bounded caller wait and fixed native-worker concurrency, without storing credentials; verify valid login, wrong password, unknown account, disabled account, PAM failure, timeout, late native success and worker exhaustion with sanitized outcomes.
- [x] 2.2 Implement Agent-owned opaque in-memory sessions and logout revocation; verify forged token, logout replay, 15-minute default and configured idle expiry, account disable/name-to-UID replacement, and Agent restart invalidation.
- [x] 2.3 Add Web login/logout and session-only secure cookie handling with anti-forgery protection; verify no persistent cookie is issued, logout clears it, stale Agent sessions require login, and an IP change alone does not invalidate a valid session.

## 3. Grants, identity resolution, and audit

- [x] 3.1 Implement Agent-owned SQLite grant and bounded audit persistence, schema versioning and atomic grant-change audit; verify empty-store bootstrap, schema mismatch, rollback on failed audit write, and policy-store outage denial without storing credentials or environment values.
- [x] 3.2 Resolve current Linux user/group identity on each Agent request and match grants by name plus UID/GID; verify user and group grants, removed membership, name/ID reuse, NSS failure and fabricated role/group fields.
- [x] 3.3 Implement `ListGrants`, `AddGrant` and `RemoveGrant` for current members of the configured host-admin group; verify permitted changes, immediate effect, unauthorized writes, malformed or nonexistent subjects, aliases/templates, protected targets and unsupported operations.
- [x] 3.4 Record sanitized Agent audit outcomes for login, logout, grant changes and service reads; verify bounded actor/operation/resource/correlation fields and absence of passwords, session credentials, request bodies and environment values.

## 4. Authorized system-service reads

- [x] 4.1 Preserve validated canonical IDs and complete alias sets in the Agent-facing inventory/inspection contract without exposing D-Bus transport details; verify aliases, concrete instances, conflicting identities and existing Serval-unit exclusion tests.
- [x] 4.2 Add Agent `ListServices` dispatch with complete-snapshot filtering by current `Service.View` and protected policy; verify ordinary user, group grant, bootstrap admin, default denial, protected aliases, privileged units, policy failures and no partial result on systemd failure.
- [x] 4.3 Add Agent `InspectService` dispatch with independent identifier validation, canonical resolution and permission/protection checks; verify canonical/alias parity, malformed and malicious names, distinct escaped/case-sensitive instances, absent/template units, unauthorized and protected targets, and unchanged systemd state.
- [ ] 4.4 Run the authorized read paths against disposable real Linux/systemd fixtures in the existing supported CI matrix; verify permitted, denied, alias, protected, malformed and cancellation/failure cases without modifying production units or revealing environment values.

## 5. Web experience and completion gates

- [x] 5.1 Add Web IPC client and authenticated Razor Pages for filtered service inventory and inspection; verify anonymous users see no service metadata, authorized users see only allowed canonical records, and sanitized failures reveal no hidden service or secret values.
- [x] 5.2 Add administrator grant-management pages calling Agent policy operations; verify current host-admin membership gates viewing and mutation, while Web-side validation bypass still fails at Agent.
- [x] 5.3 Update canonical architecture, security, code-map and deployment documentation with the implemented trust boundary, PAM/socket setup, admin group, database backup/retention, session restart behavior and compromised-Web limitation; verify instructions match the deployed configuration.
- [ ] 5.4 Run formatting, build, unit, security-negative-path and real-systemd gates; verify the OpenSpec scenarios are covered, then complete the mandatory independent post-change review with an approved verdict before treating implementation as finished.
