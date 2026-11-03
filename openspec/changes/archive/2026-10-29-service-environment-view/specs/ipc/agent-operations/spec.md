# Spec Delta

## ADDED Requirements

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
