## Purpose

Provide browser users with Linux account authentication and short lived sessions whose validity the privileged Agent can verify and revoke.

## ADDED Requirements

### Requirement: Linux account login
Serval SHALL authenticate a submitted Linux username and password through PAM and SHALL require a successful PAM account check before creating a session. Authentication SHALL NOT create a separate Serval password or retain the submitted password after the attempt.

#### Scenario: Valid account
- **WHEN** a user supplies valid credentials for an account admitted by PAM
- **THEN** Serval creates a session bound to that Linux account and starts an authenticated browser session

#### Scenario: Invalid credentials or disallowed account
- **WHEN** credentials are invalid, the account does not exist, or PAM rejects the account
- **THEN** Serval refuses login without issuing a session or disclosing which check failed

### Requirement: Agent-verifiable session
Each browser session SHALL be represented by an unguessable opaque credential whose validity and Linux account binding are verified by the Agent. The Agent SHALL reject fabricated, malformed, revoked, expired, or otherwise unverifiable credentials. A caller-supplied username, UID, role, group, or authorization flag SHALL NOT establish a session identity.

#### Scenario: Valid delegated session
- **WHEN** Web presents a valid session credential to Agent over authenticated local IPC
- **THEN** Agent derives the account identity from its own session state

#### Scenario: Fabricated identity
- **WHEN** Web presents an unknown credential with an asserted privileged username or role
- **THEN** Agent rejects the request without adopting the asserted identity

### Requirement: Non-persistent and idle-limited browser sessions
Serval SHALL issue browser session cookies without persistent-login behavior. Agent SHALL expire a session after 15 minutes without accepted authenticated activity by default; a host administrator SHALL be able to configure the idle interval. A changed client IP address SHALL NOT by itself establish or revoke a session.

#### Scenario: Inactivity
- **WHEN** the configured idle interval elapses without accepted authenticated activity
- **THEN** Agent rejects the old session credential and Web requires login again

#### Scenario: Browser closed and reopened
- **WHEN** a browser session ends and a new browser session starts
- **THEN** Serval does not intentionally restore the prior login through a persistent cookie

#### Scenario: IP address changes
- **WHEN** a valid session request arrives after the client's IP address changes
- **THEN** Agent bases acceptance on the session and current account state rather than IP equality

### Requirement: Logout, restart, and account revocation
Logout SHALL immediately revoke the Agent session. Agent restart SHALL invalidate all existing sessions. Agent SHALL stop accepting an existing session if the Linux account no longer resolves to the original identity or fails its account check.

#### Scenario: Logout
- **WHEN** a user logs out
- **THEN** the browser cookie is removed and subsequent use of the old session credential is rejected by Agent

#### Scenario: Agent restart
- **WHEN** Agent restarts while a browser still holds a session cookie
- **THEN** the old session is rejected and the user must authenticate again

#### Scenario: Account disabled or replaced
- **WHEN** a Linux account is disabled or its name resolves to a different UID after login
- **THEN** Agent rejects and revokes the session
