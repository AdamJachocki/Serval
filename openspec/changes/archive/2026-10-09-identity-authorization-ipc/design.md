## Context

See `proposal.md` for motivation and the three delta specs for behavior. `Serval.Web` is a nearly stock Razor Pages host; `Serval.Agent` is inert. `ISystemServiceInventory` and its systemd adapter already provide internal read-only list/inspect operations, but the public application result omits the resolved alias set needed for Agent policy. The adapter is not registered in either host. [ARCHITECTURE.md](../../../ARCHITECTURE.md), [docs/systemd-service-identity.md](../../../docs/systemd-service-identity.md), [docs/systemd-service-inventory.md](../../../docs/systemd-service-inventory.md), and [docs/serval-privileged-unit-exclusion.md](../../../docs/serval-privileged-unit-exclusion.md) define the existing boundaries.

## Goals / Non-Goals

**Goals:** Keep one privileged owner for authentication, sessions, grants, service policy, and auditing. Give Web only the narrow protocol and metadata it needs to present login, inventory, inspection, and grant administration.

**Non-Goals:** A reusable privileged RPC framework, client certificate or remote Agent access, persistent sessions, cross-host identity federation, broad service administration, or changing the existing systemd discovery semantics.

## Decisions

### 1. Agent owns PAM authentication and sessions

Agent exposes a dedicated `Login(username, password)` request over the authenticated Unix socket. A Linux-only, narrowly wrapped PAM conversation invokes authentication and account management under a dedicated `serval` PAM service. The login response carries a cryptographically random 256-bit opaque session credential. Agent stores only its digest, UID, account name, creation/last-activity timestamps and revocation state in memory. The credential is never written to SQLite or audit. Password buffers are cleared as far as managed/native runtime permits after PAM finishes; neither host logs request bodies. Agent runs PAM/NSS checks in a fixed-size worker pool and bounds each caller's wait. A timed-out login cannot issue a session, even if its native call later succeeds; timed-out account checks revoke the affected session. A worker slot stays occupied until its native call actually returns, so repeated hangs may make authentication unavailable without consuming an unbounded number of workers. This phase does not add a process-isolated PAM/NSS helper or guarantee termination of a stuck native call.

Each authenticated request checks the digest, idle timeout, current name-to-UID mapping and noninteractive PAM account status. UID reuse, disabled accounts, lookup failure and PAM failure revoke or reject the session. The session activity clock advances only after an accepted authenticated request. Logout removes the Agent record immediately. Agent restart destroys all sessions by design. A host-admin-owned configuration file sets the idle interval, defaulting to 15 minutes; invalid configuration fails Agent startup. The Web cookie is session-only, `Secure`, `HttpOnly`, and `SameSite=Strict`, and contains the opaque credential, not user claims. Web clears it on logout or Agent rejection and uses anti-forgery protection for state-changing browser actions. A browser close cannot be proven server-side; non-persistence means Serval does not issue a lasting cookie.

Alternative considered: Web-owned ASP.NET Identity cookies with a Web-authored user claim. This would require Agent to trust Web's claim or share a signing key with Web; a compromised Web could forge authority. Agent-owned opaque state gives immediate revocation and no persistent login, at the cost of re-login after Agent restart.

### 2. Fixed local protocol and peer identity

Agent listens on one Unix Domain Socket under a root-owned `/run/serval` directory. Installation configures a dedicated Web service UID, a dedicated Agent service, directory/socket ownership and permissions, and the selected host-admin group. On every accepted connection Agent obtains kernel peer credentials and requires the configured Web UID; socket mode and locality are additional controls, not substitutes. Agent never accepts a peer identity, Linux principal, role, groups, policy result, D-Bus member, command, or filesystem path from a message as authority.

The protocol uses length-prefixed, versioned, bounded typed messages with a strict allowlist: `Login`, `Logout`, `ListServices`, `InspectService`, `ListGrants`, `AddGrant`, `RemoveGrant`. Each request has a bounded correlation identifier and operation-specific fields only; unknown fields, invalid UTF-8, over-limit frames, duplicate fields and unsupported versions fail before dispatch. Per-connection deadlines bound socket handling and caller waits; concurrency limits cap simultaneously occupied handlers and native identity workers. These deadlines do not interrupt a native PAM/NSS call already in progress. Responses use stable sanitized result codes. No generic command, file, D-Bus or environment request exists.

Alternative considered: localhost HTTP/gRPC. Unix sockets with kernel peer credentials keep the boundary local and avoid a network listener or shared transport secret. A compromised Web process can use the credentials and sessions it possesses; neither peer UID nor this protocol eliminates that residual risk. IP address can be logged as an optional sanitized anomaly signal at Web but does not enter Agent authorization.

### 3. Agent authorization and persistent grants

Agent reads the configured host-admin group from root-owned deployment configuration and verifies its name and GID. Current membership in that group gives implicit Serval administration and `Service.View` on non-protected services. Ordinary grants are tuples of subject kind, Linux name, UID/GID at grant time, operation (`Service.View` in this phase), and canonical concrete `SystemServiceId`. Agent owns a root-accessible SQLite policy database through `Serval.Infrastructure`; Web has no database access. Storing name plus numeric identity prevents a reused name or ID alone inheriting a grant. Grant mutation and audit insertion are one transaction. The administrator UI offers list, add and remove flows; the Agent validates administrator membership, subject resolution, operation and canonical service on every call. The host-admin group is an installation choice, not a grant that Web can edit.

On each service request Agent resolves the current account and group memberships through OS facilities, reads current grants and evaluates policy. Failures in NSS/PAM, database access or policy decoding deny access. Agent does not cache successful authorization across requests. Grant changes therefore take effect on the next request. The policy model is operation-and-resource based even though only `Service.View` is currently exposed; adding another operation will require a separate protocol and spec change.

Alternative considered: Linux group membership alone or broad Serval roles. Neither gives per-service permissions or an Agent-controlled grant audit trail. Polkit rules would duplicate the Serval resource policy and are outside this change.

### 4. Resolve service identity before publication

`Serval.Systemd` remains the system-manager adapter. Extend the application-facing read model just enough to retain the adapter's validated canonical ID and full alias set for Agent policy, while the Web response maps to the existing minimal service metadata. No raw D-Bus path or transport detail crosses into `Serval.Application`. Grant creation resolves the requested service and accepts only its canonical concrete name; aliases, protected identities and privileged Serval identities cannot be granted. Direct privileged-unit names are rejected before D-Bus; aliases that resolve to privileged units remain `NotFound` as the existing adapter requires.

For inventory, Agent obtains a complete validated snapshot, then checks each canonical ID and every alias against protected policy, followed by `Service.View`. It publishes a sorted filtered list only if the underlying snapshot and all required policy evaluations succeed. For inspection, Agent validates the requested name, resolves one identity, checks all names for protection, checks permission on the canonical ID, then emits metadata. Built-in protected services are hidden from every caller in this phase, including bootstrap administrators. Existing `IsProtected` metadata is not itself an authorization decision. No environment reader is registered with these IPC operations.

Alternative considered: authorize the submitted alias or trust the Web to filter a full inventory. Both permit alias and Web-side policy bypass. The systemd adapter already has the authoritative names; preserving them avoids a second, racy resolution path.

### 5. Audit and failure semantics

Agent writes bounded, structured, non-secret audit records for login outcome, logout, grant changes, and service-access decisions to its protected SQLite store. Records contain verified actor when available, operation, canonical service when known, outcome, bounded correlation ID and timestamp. They exclude passwords, session credentials, request bodies, environment values and arbitrary exception text. A failed audit write fails a grant mutation atomically and denies a service operation; login also fails without a recorded outcome. The Web receives generic login failure and sanitized authorization or availability results. Missing/forged sessions, protected targets, malformed requests, ambiguous systemd replies and policy-store failures never yield partial metadata.

Alternative considered: best-effort application logging as the only audit channel. Logger failures are difficult to detect, and grant changes would lack durable accountability. The local SQLite audit is explicit and can fail closed; retention and backup access are documented as operator responsibilities.

### 6. Scope of privileged capability and threats

Attacker-controlled inputs are browser credentials, session cookie, service ID, grant subject/operation, correlation ID, message framing and timing; systemd and NSS/PAM replies are also untrusted. The new root capability is limited to PAM checks, root-owned policy/audit state, and the existing read-only system-manager metadata calls. `ListServices` takes no resource selector; `InspectService` takes one bounded service name; grant operations take one validated subject, one canonical service and the fixed operation. Returned service data is canonical ID, description and states only.

Agent validates inputs before systemd or policy lookup, uses the existing typed D-Bus adapter rather than a process or shell, and never accepts a caller-supplied path. The SQLite path and socket path are fixed in root-owned configuration; deployment provisions ownership and avoids symlinked policy/socket locations. These controls address command injection, traversal, symlink substitution, arbitrary write, arbitrary service targeting and Web-as-deputy confusion. Read-only systemd calls do not mutate units, reload the daemon, or start services. Future write operations need a separate threat analysis and contract.

## Risks / Trade-offs

- [Compromised Web can replay sessions or submitted passwords it sees] → Run Web as its own unprivileged UID, limit IPC operations and session lifetime, revoke on logout/restart, and document this trust limit. Agent cannot distinguish a legitimate use of a stolen live credential by the same Web peer.
- [PAM/account and NSS lookups may be slow or unavailable] → Bound caller waits and native-worker concurrency, fail closed, and show a sanitized unavailable result. A timed-out native call may keep its worker and credential buffer until it returns; enough such calls can make login/account checks unavailable until those calls finish or Agent restarts. Browser refresh does not cancel them. Process isolation is deferred by explicit product decision.
- [SQLite outage blocks access, including bootstrap administration] → Treat database health as a deployment prerequisite; document repair and backup procedures. No bypass mode is added.
- [Audit volume and policy DB growth] → Bound individual records and document retention/rotation; review throughput before shipping. A failed required audit append denies rather than silently dropping records.
- [Systemd identity may change between resolution and a later action] → This phase is read-only; any future mutation must resolve and authorize its own current identity again.

## Migration Plan

1. Add Agent-owned configuration, PAM service definition, policy database initialization and Linux service/socket provisioning. Verify Web UID and admin group at startup; fail rather than defaulting to root or an arbitrary group.
2. Deploy Agent before Web. An empty grant store is safe because only verified members of the configured host-admin group can administer grants or view non-protected services.
3. Enable Web login and service pages only when Agent health and IPC configuration are valid. Existing unauthenticated scaffold pages cease being the service entry point.
4. Rollback by stopping Web/Agent and restoring the prior binaries/configuration. Agent sessions are intentionally lost. Preserve or back up the policy database before schema migration; do not silently downgrade an unknown schema.

The durable IPC, session, grant, audit, PAM and deployment decisions should be promoted into the relevant canonical documents under `docs/` during implementation, with `docs/code-map.md`, `ARCHITECTURE.md` and `SECURITY.md` adjusted only where their current descriptions change.
