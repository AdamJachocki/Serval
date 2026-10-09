# Agent Operations Specification

## Purpose

Expose a small local Web-to-Agent protocol that authenticates its peer and delegated user before returning authorized service metadata.

## Requirements

### Requirement: Authenticated local transport
Agent SHALL accept requests only through local IPC from the configured unprivileged Web process identity. Agent SHALL authenticate the peer independently of message fields and reject other local processes, malformed frames, unsupported operations, and over-limit requests without invoking privileged operations.

#### Scenario: Configured Web peer
- **WHEN** a connection is made by the configured Web process identity and carries a valid typed request
- **THEN** Agent processes that request under the protocol's per-operation checks

#### Scenario: Untrusted local peer
- **WHEN** another local process connects or claims to be Web in the message
- **THEN** Agent rejects the connection without performing a service or policy operation

#### Scenario: Malformed request
- **WHEN** a request has an invalid frame, oversized payload, unknown operation, or unexpected identity field
- **THEN** Agent rejects it with a bounded sanitized failure and performs no privileged action

### Requirement: Delegated user authority
Every service read and policy-management request SHALL carry an Agent-verifiable user session. Agent SHALL determine the user and current permissions itself on each request and SHALL reject missing, expired, revoked, or forged sessions. Authentication requests and logout SHALL use their dedicated narrow contracts.

#### Scenario: Missing delegation
- **WHEN** Web requests service inspection without a valid session credential
- **THEN** Agent rejects the request before returning service metadata

#### Scenario: Web validation bypass
- **WHEN** Web sends a request that its UI would have blocked
- **THEN** Agent's own input, identity, authorization, and protected-target checks determine the result

### Requirement: Authorized service inventory
Agent SHALL offer a read-only inventory of existing system-level concrete services. For each record, Agent SHALL apply current `Service.View` permission and protected-service policy before response publication. Agent SHALL return only canonical identity, description, and load, active, and sub-state metadata; it SHALL return no environment values, credentials, or partial inventory after an underlying failure.

#### Scenario: Filtered list
- **WHEN** a user requests the inventory with grants for only some non-protected services
- **THEN** the response contains only those authorized canonical services in deterministic order

#### Scenario: Underlying inventory failure
- **WHEN** systemd discovery fails or returns ambiguous identity data
- **THEN** Agent returns a sanitized failure and no partial list

### Requirement: Authorized service inspection
Agent SHALL offer read-only inspection of one validated concrete system service or alias. Agent SHALL resolve its canonical identity, apply `Service.View` and protected-service policy to that identity and every validated alias, and only then return the canonical metadata. It SHALL NOT read or return service environment data or trigger service lifecycle or configuration changes.

#### Scenario: Authorized inspection
- **WHEN** a user inspects an authorized non-protected service by canonical name or valid alias
- **THEN** Agent returns its canonical identity, description, and state metadata

#### Scenario: Denied or protected inspection
- **WHEN** a user inspects an unauthorized or protected service
- **THEN** Agent returns no service metadata and no indication that can be used to bypass the policy through an alias

#### Scenario: Invalid or absent service
- **WHEN** a user supplies a malformed name, uninstantiated template, or nonexistent service
- **THEN** Agent returns a bounded failure or absence result without exposing unrelated metadata or changing systemd state
