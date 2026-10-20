## Purpose

Provide consistent authenticated identity, protected page access and browser login behavior while Linux accounts and Agent sessions remain authoritative.

## ADDED Requirements

### Requirement: Single protected authentication ticket
Web SHALL issue one authentication cookie containing a confidentiality- and integrity-protected ticket that carries the Agent session credential and minimal confirmed user identity. The credential SHALL NOT be exposed as a user claim, in rendered content, URLs, logs, telemetry, or policy storage. The cookie SHALL be Secure, HttpOnly, SameSite=Strict, host-only, Path=/ and non-persistent. Existing antiforgery protection SHALL remain separate and enabled.

#### Scenario: Successful sign-in
- **WHEN** Agent successfully authenticates a Linux account and returns its session and confirmed identity
- **THEN** Web issues one protected authentication cookie without a separate raw-session cookie or persistent-login expiration attributes

#### Scenario: Tampered or legacy cookie
- **WHEN** a browser presents a tampered ticket or the previous raw-session cookie format
- **THEN** Web does not authenticate it or fall back to trusting the raw value

### Requirement: Current verified identity
Web SHALL establish authenticated request identity only after Agent confirms the session and current Linux account. Missing credentials, invalid identity responses and failed or unavailable session verification SHALL invalidate Web authentication and clear its authentication cookie. There SHALL be no cross-request positive validation cache.

#### Scenario: Verified identity
- **WHEN** Agent confirms a valid session for a current Linux account
- **THEN** Web makes that account's verified name and identifier available to the page and authorization pipeline

#### Scenario: Verification failure
- **WHEN** the session expires, is revoked, is lost on restart, fails account checks, or Agent cannot complete verification
- **THEN** Web does not execute the protected page handler and initiates the normal login challenge

### Requirement: Protected page access and navigation
Service and grant pages SHALL require authenticated identity through a common authorization pipeline. Unauthenticated browser navigation to those pages SHALL redirect to login. Login, error handling and public assets SHALL remain reachable without a valid session. Navigation SHALL show sign-in for anonymous users and verified identity with sign-out for authenticated users. Existing Agent authorization SHALL remain mandatory regardless of UI visibility or Web authentication.

#### Scenario: Anonymous protected-page request
- **WHEN** an anonymous browser requests a service or grant page
- **THEN** it is redirected to login without protected data or handler execution

#### Scenario: Agent unavailable during challenge
- **WHEN** session verification cannot reach Agent
- **THEN** the browser can reach the login page without a redirect loop or dedicated outage page

#### Scenario: Authenticated but unauthorized
- **WHEN** an authenticated user requests an operation denied by Agent
- **THEN** no unauthorized data or mutation is allowed and the denial does not by itself turn into a login challenge

#### Scenario: Identity-aware layout
- **WHEN** a page is rendered for an anonymous or authenticated user
- **THEN** its sign-in or sign-out controls reflect the verified request identity and any displayed account name is HTML-encoded

### Requirement: Safe login outcomes
Login SHALL show a sanitized Agent-problem message when authentication cannot complete because Agent is unavailable, and SHALL use one generic authentication-failure message for invalid credentials or disallowed accounts. Failed login SHALL NOT issue an authentication ticket or retain submitted passwords in the rendered response. A successful login SHALL return only to a validated local destination, defaulting to the service list.

#### Scenario: Agent unavailable during login
- **WHEN** a login submission encounters an Agent connection failure, timeout or unavailable result
- **THEN** the login page shows a sanitized Agent-problem message without a ticket, transport details or credentials

#### Scenario: Credentials rejected
- **WHEN** the account is unknown, credentials are invalid or PAM disallows the account
- **THEN** login fails with the same generic message without revealing which check failed

#### Scenario: Malicious return destination
- **WHEN** a successful login includes an external or malformed return destination
- **THEN** Web redirects to the default local service list instead

#### Scenario: Cross-site form submission
- **WHEN** login, logout or a grant mutation lacks valid antiforgery proof
- **THEN** the request is rejected without performing the corresponding operation
