## ADDED Requirements

### Requirement: Agent-confirmed session identity
Agent SHALL provide a narrow session-validation operation accepting only the protocol envelope, correlation identifier and opaque session credential. It SHALL verify the configured local peer, session, current account status and Linux identity, including resolving current group membership, before returning the confirmed account name and UID. Successful login SHALL also return confirmed account name and UID alongside its newly issued session. Session validation SHALL NOT return credentials, group lists, service grants, environment values or caller-selected identity data, and SHALL NOT renew idle activity. It SHALL preserve bounded secret-free auditing and fail closed if required identity or audit checks fail.

#### Scenario: Valid session identity
- **WHEN** the configured Web peer submits a valid session-validation request
- **THEN** Agent returns the session-bound name and UID after current account resolution, without creating a session or renewing activity

#### Scenario: Login identity
- **WHEN** PAM authentication and account checks succeed and Agent creates a session
- **THEN** login returns the verified Linux name and UID with that session

#### Scenario: Invalid or replaced identity
- **WHEN** a validation credential is forged, malformed, expired or revoked, or the account is disabled or resolves to a different identity
- **THEN** Agent returns no confirmed identity and refuses authentication

#### Scenario: Caller-supplied privilege or identity
- **WHEN** a validation frame includes unexpected username, UID, group, role or authorization fields, or comes from an untrusted local peer
- **THEN** Agent rejects the request before returning identity data

#### Scenario: Verification infrastructure failure
- **WHEN** account resolution or required audit persistence cannot complete reliably
- **THEN** Agent returns a bounded failure without identity or secret diagnostic data

#### Scenario: Validation does not grant operation access
- **WHEN** a caller validates a session and subsequently requests an unauthorized or protected service or unauthorized grant change
- **THEN** Agent independently checks current authorization for that operation and refuses the forbidden access
