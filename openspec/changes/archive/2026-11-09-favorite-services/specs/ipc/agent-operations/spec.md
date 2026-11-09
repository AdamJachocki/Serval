# Spec Delta

## MODIFIED Requirements

### Requirement: Authorized service inventory
Agent SHALL offer a read-only inventory of existing system-level concrete services. For each record, Agent SHALL apply current `Service.View` permission and protected-service policy before response publication. Agent SHALL return only canonical identity, description, and load, active, and sub-state metadata, plus whether each returned service is a favorite of the caller; it SHALL return no environment values, credentials, or partial inventory after an underlying failure.

#### Scenario: Filtered list
- **WHEN** a user requests the inventory with grants for only some non-protected services
- **THEN** the response contains only those authorized canonical services in deterministic order

#### Scenario: Underlying inventory failure
- **WHEN** systemd discovery fails or returns ambiguous identity data
- **THEN** Agent returns a sanitized failure and no partial list

#### Scenario: Favorite membership
- **WHEN** a user with stored favorites requests the inventory
- **THEN** the response marks exactly those returned services that are the caller's favorites
- **AND** it names no favorite outside the returned authorized records

#### Scenario: Favorites store failure
- **WHEN** the caller's favorites cannot be read reliably
- **THEN** Agent returns a sanitized unavailable failure and no partial list or partial favorite membership

## ADDED Requirements

### Requirement: Add favorite operation
Agent SHALL provide a typed add-favorite operation accepting only a session, correlation ID and one concrete service name or alias. It SHALL authenticate the session, validate the name, resolve the canonical identity, apply protected-service policy to the identity and every validated alias, and require current `Service.View` before storing. Results SHALL be success, invalid request, not found, limit reached, unauthenticated or unavailable.

#### Scenario: Authorized add
- **WHEN** a user adds an authorized non-protected service by canonical name or valid alias
- **THEN** Agent stores the canonical favorite for the verified caller and returns success

#### Scenario: Web validation bypass
- **WHEN** Web submits an add for an unauthorized, protected, nonexistent or alias-masked protected service
- **THEN** Agent returns the same not-found result for each, stores nothing and returns no service metadata

#### Scenario: Malformed add
- **WHEN** the request has a missing or invalid session, a malformed or over-length service name, an uninstantiated template, duplicate or unknown fields, or an identity field
- **THEN** Agent rejects it before any favorite lookup or write

### Requirement: Remove favorite operation
Agent SHALL provide a typed remove-favorite operation accepting only a session, correlation ID and one concrete service name or alias. After authenticating the session and validating the name grammar, it SHALL delete only the verified caller's favorite for the resolved canonical service, or for the submitted concrete name when it no longer resolves, and SHALL return the same success result regardless of prior favorite state, service existence or authorization.

#### Scenario: Remove own favorite
- **WHEN** a user removes one of their favorites
- **THEN** only that user's favorite for the canonical service is deleted and Agent returns success

#### Scenario: Remove cannot probe
- **WHEN** a user removes a nonexistent, protected or unauthorized service with a well-formed name
- **THEN** the result is identical to removing an existing non-favorite authorized service

#### Scenario: Malformed remove
- **WHEN** the request has an invalid session, a malformed service name, or unexpected fields
- **THEN** Agent rejects it without changing any favorite

### Requirement: Favorite operations stay off systemd state
Favorite operations SHALL use only Agent-owned application storage and read-only service identity resolution. They SHALL NOT change service lifecycle, unit files, drop-ins, environment sources or manager reload state, and SHALL NOT read environment data.

#### Scenario: Favorite side effects
- **WHEN** a favorite is added or removed successfully or unsuccessfully
- **THEN** systemd configuration, service state and environment data remain unchanged and unread
