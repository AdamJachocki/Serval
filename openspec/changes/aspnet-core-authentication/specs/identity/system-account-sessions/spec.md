## MODIFIED Requirements

### Requirement: Agent-verifiable session
Each browser session SHALL carry an unguessable opaque Agent credential inside its single protected Web authentication ticket. The credential's validity and Linux account binding SHALL be verified by Agent independently of the Web ticket. Agent SHALL reject fabricated, malformed, revoked, expired, or otherwise unverifiable credentials. A caller-supplied username, UID, role, group, or authorization flag SHALL NOT establish a session identity.

#### Scenario: Valid delegated session
- **WHEN** Web extracts the credential from its protected ticket and presents it to Agent over authenticated local IPC
- **THEN** Agent derives the account identity from its own session state

#### Scenario: Fabricated identity
- **WHEN** Web presents an unknown credential with an asserted privileged username or role
- **THEN** Agent rejects the request without adopting the asserted identity

#### Scenario: Valid Web ticket with invalid Agent credential
- **WHEN** a protected Web ticket still exists but its Agent credential is no longer valid
- **THEN** the ticket does not establish authenticated access and the user must log in again

### Requirement: Non-persistent and idle-limited browser sessions
Serval SHALL issue browser session cookies without persistent-login behavior. Agent SHALL expire a session after 15 minutes without accepted authenticated activity by default; a host administrator SHALL be able to configure the idle interval. A changed client IP address SHALL NOT by itself establish or revoke a session. Validation-only requests, public asset requests and Web ticket renewal SHALL NOT renew Agent activity. Existing accepted service-read and grant-management operations SHALL continue to renew activity. Web ticket lifetime SHALL NOT permit access after Agent expiry or impose a shorter independent inactivity limit on a still-valid Agent session.

#### Scenario: Inactivity
- **WHEN** the configured idle interval elapses without accepted authenticated activity
- **THEN** Agent rejects the old session credential and Web requires login again

#### Scenario: Browser closed and reopened
- **WHEN** a browser session ends and a new browser session starts
- **THEN** Serval does not intentionally restore the prior login through a persistent cookie

#### Scenario: IP address changes
- **WHEN** a valid session request arrives after the client's IP address changes
- **THEN** Agent bases acceptance on the session and current account state rather than IP equality

#### Scenario: Repeated validation without accepted operations
- **WHEN** only session-validation or asset requests occur throughout the configured idle interval
- **THEN** the Agent session still expires

#### Scenario: Accepted activity
- **WHEN** accepted service or grant operations continue within the configured idle interval
- **THEN** Agent renews session activity and Web ticket handling does not prematurely end the valid session
