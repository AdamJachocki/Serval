## Why

Serval currently reads and writes its authentication cookie directly in page models, leaving ASP.NET Core without an authenticated `ClaimsPrincipal`. Integrating the existing Linux-backed sessions with the framework will provide consistent page protection, identity-aware Razor rendering and standard authorization extension points.

## What Changes

- Use ASP.NET Core cookie authentication and authorization without ASP.NET Core Identity, application passwords, or Microsoft Entra.
- Store the opaque Agent session credential in the protected authentication ticket's `AuthenticationProperties.Items`; issue only one authentication cookie.
- Populate `ClaimsPrincipal` from Agent-confirmed Linux identity and validate sessions through the Agent on authenticated Web requests.
- Add a narrow session-validation IPC operation and return confirmed identity with successful login.
- Replace repeated page-level cookie checks with framework authorization and use authenticated identity in Razor navigation.
- Preserve Agent-enforced service grants, current account/group checks, session inactivity, logout revocation and restart invalidation.
- Challenge unauthenticated protected-page requests through the standard cookie login redirect, including failed session validation caused by Agent unavailability. Show a sanitized Agent-problem message when login itself cannot complete because the Agent is unavailable.
- **BREAKING**: Replace the raw cookie format with a protected framework ticket and extend the IPC contract; existing browser logins require reauthentication and Web/Agent must be deployed together.

Non-goals: new roles or permissions, permission-management redesign, cloud identity, registration/reset-password flows, generic authorization IPC, outage/retry pages, and service or systemd behavior changes.

## Capabilities

### New Capabilities

- `identity/web-authentication`: Framework cookie tickets, principals, protected Razor Pages, authentication-aware UI and login failure behavior.

### Modified Capabilities

- `identity/system-account-sessions`: Clarify the single protected browser ticket carrying the Agent credential and preserve Agent-owned lifetime without validation-only activity renewal.
- `ipc/agent-operations`: Add narrow Agent-confirmed identity responses for login and session validation without transferring authorization authority to Web.

## Impact

- `Serval.Web`: startup, cookie events, current-session access, login/logout, protected page models and shared layout.
- `Serval.Application/Ipc`: typed requests/results and bounded wire codec.
- `Serval.Agent`: session-validation dispatch, current identity resolution and audit integration.
- Application, Agent and Web tests, including real PAM/IPC integration and browser-facing authentication tests.
- Canonical architecture, security, code-map and identity deployment documentation must reflect the new ticket and IPC flow.
- Uses the existing ASP.NET Core framework and Data Protection setup; no new user database or identity-provider dependency. `Serval.Domain`, grant persistence and systemd adapters retain their current responsibilities.
