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

### Requirement: Authorized environment metadata operation
Agent SHALL provide a typed metadata operation accepting a session, correlation ID and concrete service name or alias. It SHALL validate identity, canonical service, aliases, protection and `Environment.View` before sensitive acquisition. Success SHALL return canonical identity, scope, ordered source metadata, sorted variable names with winning-source IDs and current reveal eligibility, without values, paths, lengths or hashes.

#### Scenario: Authorized metadata
- **WHEN** a permitted caller requests metadata for a supported service
- **THEN** the complete supported-declarations metadata is returned without values, including a successful empty result

#### Scenario: Alias and authorization boundary
- **WHEN** Web bypasses UI checks and submits an unauthorized or protected service alias
- **THEN** Agent denies the operation before acquiring environment contents

### Requirement: Authorized single-variable reveal operation
Agent SHALL provide a typed reveal operation accepting a session, correlation ID, concrete service name or alias, and one variable name. It SHALL require both environment permissions before reading and SHALL perform a fresh complete supported-declarations read. Success SHALL disclose only the requested current value, canonical identity, scope and its current winning-source label. No bulk or arbitrary-file operation SHALL be exposed.

#### Scenario: Reveal changed declaration
- **WHEN** configuration changed after the metadata request and the fresh read is consistent
- **THEN** the response contains the requested variable's current value and source label from that read, not the previous metadata snapshot

#### Scenario: Variable absent
- **WHEN** the requested variable is absent from a successful current read
- **THEN** the operation returns sanitized absence without any other value

#### Scenario: Empty value
- **WHEN** the requested variable is present with an empty value
- **THEN** success remains distinguishable from absence

### Requirement: Bounded secret transport and safe failures
Environment operations SHALL preserve existing session and peer authentication, strict framing and request limits. Oversized results SHALL fail without truncation or partial publication. Values SHALL cross IPC only in the dedicated successful reveal response and SHALL be absent from ordinary serialization, diagnostics and errors. Source failures, cancellation and inconsistent reads SHALL publish no partial environment data.

#### Scenario: Malicious request
- **WHEN** a request has duplicate or unknown fields, invalid session, malformed service, path-like variable name, control characters or excessive size
- **THEN** it is rejected before sensitive acquisition and exposes no contents

#### Scenario: Read or transport failure
- **WHEN** a source is missing, unsupported, unsafe or changing, or a deadline or response-size limit is exceeded
- **THEN** a bounded sanitized failure is returned without partial names or values

#### Scenario: Secret diagnostics
- **WHEN** a reveal succeeds or fails during serialization, cancellation or socket writing
- **THEN** diagnostics and audit remain value-free and owned secret buffers are released on all exits

### Requirement: Preserve ordinary service reads
Existing inventory and service inspection SHALL remain value-free and SHALL NOT acquire environment data. Environment operations SHALL NOT mutate source files, service configuration, lifecycle or manager reload state.

#### Scenario: Existing client
- **WHEN** a client uses the existing service inventory or inspection operation
- **THEN** its permission requirements and response remain unchanged and no environment read occurs

#### Scenario: Read-only environment flow
- **WHEN** either environment operation completes or fails
- **THEN** source contents, configuration, service lifecycle and daemon-reload state remain unchanged
