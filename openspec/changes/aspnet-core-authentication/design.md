## Context

See `proposal.md` for motivation. `Serval.Web/Program.cs` currently has authorization middleware but no authentication registration or middleware. `ServalSessionCookie` stores a raw Agent credential; page models repeat reads, clearing and redirects. The layout renders sign-out without authenticated identity.

`AgentSessions.AuthenticateAsync` verifies sessions without refreshing activity. `AgentDispatcher` separately records successful service/grant operations, resolves current accounts and enforces grants. Login currently returns only a token. `AgentIpcClient` maps transport failures to operation-specific `Unavailable` results with a bounded timeout.

The trust boundary and accepted permission model remain those in `ARCHITECTURE.md`, `SECURITY.md`, `docs/identity-deployment.md` and `authorization/system-service-access`. This change requires a design because it crosses Web, Application IPC and Agent authentication.

## Goals / Non-Goals

**Goals:** integrate Agent authentication with framework tickets and principals; centralize page protection; retain independent Agent enforcement and current lifetime rules; give future policy-based permissions a standard Web integration point.

**Non-Goals:** implement future permission policies, mirror the grant database in Web, put group/service lists in tickets, introduce a generic permission-query operation, or change systemd operations. Full Identity and cloud login are excluded by the proposal.

## Decisions

### 1. Use the built-in cookie scheme and its events

Register `AddAuthentication(...).AddCookie(...)`, authorization services and `UseAuthentication()` before `UseAuthorization()`. A scoped `CookieAuthenticationEvents.ValidatePrincipal` uses the Agent client; no custom authentication handler or ticket cryptography is required.

Retain cookie name `__Host-ServalSession`, with Secure, HttpOnly, SameSite=Strict, Path=/, no Domain and `IsPersistent=false`. Store the Agent credential under one namespaced `AuthenticationProperties.Items` key, never a claim or `Parameters` entry. Reuse the existing persisted Data Protection keys and Testing ephemeral provider. Keep the ticket minimal and verify that it produces one physical authentication cookie; do not add a ticket store, TempData cookie or group/permission payload for this feature. Antiforgery remains separate.

Alternative rejected: a custom handler over a raw cookie would repeat framework behavior and does not match the requested standard protected ticket.

Framework references: [cookie authentication and validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0) and [persisted authentication properties](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.authenticationproperties.items?view=aspnetcore-10.0).

### 2. Add only the identity contract the Web needs

Add `ValidateSession(correlationId, session)` and a corresponding result containing status and, on success only, a bounded identity DTO `(Name, Uid)`. Extend successful `Login` with the same DTO. The DTO belongs to Application IPC and has no ASP.NET types.

The new operation follows the existing socket peer check and strict request grammar. Use existing token/correlation bounds. Resolve session identity through `AgentSessions` and current account/group resolution through the same path used for authorized operations; reject name/UID mismatch or lookup failure. Return neither groups nor computed permissions. Reuse bounded sanitized audit handling for validation outcomes; required audit failures return failure rather than publish identity. Validation never calls `RecordAcceptedActivity`.

Bump the protocol version for the incompatible successful-login response. Update all typed callers, fixtures and failure-result mappings together. Reject missing, malformed, oversized or inconsistent successful identity payloads on Web; do not construct a principal from submitted login input. The configured local Agent is the authority for the returned identity.

Alternative rejected: infer identity from the login form or merely decode cookie claims. Neither confirms current session/account state.

### 3. Use a minimal principal and revalidate it

Use the scheme as the authenticated `ClaimsIdentity.AuthenticationType`, `ClaimTypes.Name` for the confirmed Linux name and `ClaimTypes.NameIdentifier` for the invariant-decimal UID. These identify the request; a UID alone is not a grant key. Preserve name/UID binding in Agent and do not invent roles or copy Linux groups into role claims.

For each request that authenticates a ticket, validate its credential through Agent without cross-request caching. Missing token, failed validation or invalid response invokes `RejectPrincipal` and `SignOutAsync`. On success, compare the name/UID binding with the ticket and reject mismatches; rebuild the minimal principal from confirmed identity so extra ticket claims cannot become authority. Public assets do not depend on authenticated identity or refresh activity.

Page models obtain the token from the successful authentication ticket through a small Web-local scoped accessor, not direct cookie parsing. Repeated reads within one request reuse the framework authentication result. Service/grant operations still revalidate and authorize independently in Agent: validation success is not a delegation of unchecked authority. If the operation returns `Unauthenticated` after initial validation, centralize sign-out and challenge handling to cover the race.

Alternative rejected: a principal cached until cookie expiration would delay revocation and group/grant changes.

### 4. Keep Agent lifetime authoritative

Use a one-day sliding framework ticket lifetime and a session-only browser cookie. This is an envelope lifetime, not a new session timeout: each accepted principal still requires live Agent validation. Its sliding renewal threshold (12 hours) is longer than the maximum Agent inactivity window (120 minutes), leaving ample renewal opportunities for continuously active sessions without an earlier Web timeout.

Only the existing accepted service/grant operations refresh Agent activity. Neither validation nor sliding ticket renewal refreshes it. Test configured idle windows, continuous accepted activity across ticket renewal, repeated validation-only traffic, restart, disabled/replaced account, and changed IP using controlled time.

Alternative rejected: copying the default 15-minute idle timeout into a separate cookie timeout would create competing clocks and prematurely reject sessions when the host uses another configured interval.

### 5. Centralize page protection and keep normal challenges

Apply authenticated-user authorization conventions to `/Services` and `/Grants`; protect logout POST while leaving login, existing public pages, error handling and assets anonymous. Use standard cookie challenge redirects for browser Razor Pages; do not globally rewrite all 401 responses or change future API semantics.

The layout uses `User.Identity.IsAuthenticated`, renders the encoded name and sign-out only for authenticated users, and sign-in otherwise. Existing grant/service decisions stay in Agent; authenticated navigation is not proof of permission. Standard policies and `IAuthorizationService` remain available for future resource-based rules, without adding unused policy abstractions now. Preserve existing forbidden/not-found behavior for actual resource denials.

Login uses `SignInAsync` after a complete successful Agent result. Validate return URLs as local; use the service list otherwise. Remove password values from both the bound model and ModelState before redisplay. `Unavailable` produces a sanitized Agent-problem message; rejected credentials/account checks share a generic message. No outage page, background retry or availability monitor is added.

Preserve POST/antiforgery logout. Revoke the Agent session before confirming logout, then call `SignOutAsync`; `Unauthenticated` also permits local cleanup. If revocation fails after successful initial validation, retain the current sanitized "sign out could not be confirmed" behavior rather than claiming remote revocation. If initial validation itself fails, clear Web authentication and challenge as specified; this does not assert that an unreachable Agent revoked its credential.

### 6. Privileged operation analysis

| Concern | Design |
| --- | --- |
| Attacker-controlled inputs | Browser ticket, login credentials, return URL, session token, correlation ID, all IPC frame fields and forged identity assertions; socket peer metadata is obtained from the kernel. |
| Required capability | Confirm an already-issued session through privileged account checks. No new filesystem, process, service or root mutation capability. |
| Smallest contract | Fixed validation request and name/UID-only successful response; confirmed identity added to login. |
| Trust basis | Kernel-verified Web peer plus Agent-owned session binding and current PAM/NSS account checks. Web claims/roles never authorize Agent operations. |
| Validation and failure | Strict frame and identity bounds; reject unexpected fields, malformed tokens, mismatches, unavailable checks and audit failure. Secret-free actor/operation/outcome/correlation audit only. |
| Protected resources | Validation has no service/path input and returns no service data. Existing operation-specific authorization and protected-target checks remain required after validation. |
| Injection / traversal / symlink / TOCTOU | No new command or path resolution. Reuse current PAM/NSS boundary. A successful validation cannot replace the later operation's current identity and policy checks. |
| Secret exposure / confused deputy | Keep token only in protected ticket properties and bounded IPC; exclude cookies, properties and credentials from logs. Never accept an actor name as authority or expose generic authorization APIs. |

Required negative tests include forged and expired credentials, malicious extra fields, untrusted peer, unavailable checks, missing/invalid identity responses, privilege claims in a ticket, and service/grant authorization bypass attempts after successful validation.

## Risks / Trade-offs

- Additional Agent validation and account lookups per authenticated request -> reuse only within a request, preserve deadlines/worker bounds, and do not cache away revocation guarantees.
- Session loss on any validation outage -> intentional login challenge; a subsequent login failure gets the agreed message.
- A lost logout connection cannot guarantee immediate remote revocation -> do not report successful logout without acknowledgement; preserve Agent expiry and restart rules.
- The Web key ring now protects authentication tickets as well as antiforgery -> retain restrictive existing key-directory access and document its expanded role.
- A compromised Web can read tickets and reuse observed credentials -> unchanged trust limitation; Agent continues to enforce current permissions independently.
- Cookie renewal and antiforgery identity changes can interact -> test old anonymous login forms, authenticated POSTs and sign-out with actual middleware, not only direct PageModel tests.

## Migration Plan

Deploy the matching Web and Agent binaries together, restarting Agent so previous sessions are invalidated. Use the same cookie name with no legacy fallback; old raw cookies fail ticket validation and lead to login. No grant database migration or user-account conversion is needed.

Rollback deploys the previous matching pair and restarts Agent; users authenticate again. No dual-version IPC or cookie compatibility mode is introduced.

During implementation update `ARCHITECTURE.md`, `SECURITY.md`, `docs/code-map.md` and `docs/identity-deployment.md` at their canonical sections. Record the protected-ticket/Agent-authority decision there, including Data Protection key responsibility, the new operation, coordinated deployment and forced re-login. Do not copy these documents into the change.
