# service-favorites Specification

## Purpose

Let each authenticated Linux user keep a durable personal set of favorite system services that Serval presents first, without granting or revealing any access beyond existing service authorization.

## Requirements

### Requirement: Per-user favorite ownership
Agent SHALL store favorites as durable non-secret state bound to the caller's verified Linux user name and resolved UID, and to the canonical concrete service identity. A favorite SHALL be visible and removable only by the identity that created it. A user with the same name but a different UID, or the same UID under a different name, SHALL NOT inherit it.

#### Scenario: Separate users
- **WHEN** user alice marks nginx.service as a favorite and user bob, who is also authorized for nginx.service, lists services
- **THEN** nginx.service is not reported as a favorite for bob

#### Scenario: Reused account name
- **WHEN** an account is deleted and a new account is created with the same name but a different UID
- **THEN** the new account sees none of the previous account's favorites

#### Scenario: Persistence across sessions
- **WHEN** a user marks a service as a favorite, logs out, and the Agent restarts before the next login
- **THEN** the service is still reported as that user's favorite after the next login

### Requirement: Canonical favorite identity
Agent SHALL resolve a submitted service name or alias to its canonical concrete identity before storing a favorite. Each user SHALL have at most one favorite per canonical service. Repeating an add for the same service, directly or through an alias, SHALL succeed without creating a duplicate.

#### Scenario: Add through alias
- **WHEN** a user adds a favorite using a valid alias of an authorized service
- **THEN** the canonical service is stored and reported as the favorite

#### Scenario: Repeated add
- **WHEN** a user adds the same authorized service twice
- **THEN** both requests succeed and the user has exactly one favorite for that service

### Requirement: Favorites never widen visibility
Agent SHALL accept a favorite only for a service that currently exists, is not protected and for which the caller currently holds `Service.View`. A rejected add SHALL return the same result for unauthorized, protected and nonexistent services and SHALL store nothing. Favorites SHALL be reported only for services included in the caller's current authorized inventory.

#### Scenario: Unauthorized, protected or absent target
- **WHEN** a user submits an add for a service they cannot view, a protected service, or a nonexistent service
- **THEN** each request returns the same not-found result and no favorite is stored

#### Scenario: Access revoked after favoriting
- **WHEN** a user's `Service.View` grant for a favorite service is removed
- **THEN** that service no longer appears in the user's inventory or favorites
- **AND** it appears as a favorite again if access is later restored and the stored favorite was not removed

#### Scenario: Service removed from host
- **WHEN** a favorite service no longer exists on the host
- **THEN** the inventory succeeds without it and reports no favorite for it

### Requirement: Removing favorites
Agent SHALL remove only the caller's own favorite for the resolved canonical service. Remove SHALL be idempotent. For any well-formed concrete service name, it SHALL return the same success result whether or not the favorite existed, the service exists, or the caller can view it, so remove cannot be used to probe service existence or authorization.

#### Scenario: Remove existing favorite
- **WHEN** a user removes a service from favorites
- **THEN** the service is no longer reported as that user's favorite and remains in the inventory as a normal service

#### Scenario: Remove non-favorite or hidden service
- **WHEN** a user removes a service that is not a favorite, does not exist, or is not visible to them
- **THEN** the request returns the same success result and no other user's favorite changes

### Requirement: Bounded favorites
Agent SHALL limit each user to 100 stored favorites. An add that would exceed the limit SHALL be rejected with a distinct limit result, store nothing and leave existing favorites unchanged. Favorites that are hidden because access was revoked still count toward the limit.

#### Scenario: Limit reached
- **WHEN** a user with 100 stored favorites adds another authorized service
- **THEN** the request is rejected with the limit result and the stored favorites are unchanged

#### Scenario: Re-adding at the limit
- **WHEN** a user with 100 stored favorites adds a service that is already one of them
- **THEN** the request succeeds without changing the count

### Requirement: Favorite operation audit and failure
Agent SHALL audit favorite add and remove outcomes with the verified actor, operation, canonical service when known, outcome and correlation identifier. If favorite storage or audit is unavailable, the operation SHALL fail with a sanitized unavailable result and leave stored favorites unchanged.

#### Scenario: Audited add
- **WHEN** a user successfully adds a favorite
- **THEN** an audit record names the actor, the add operation, the canonical service and success

#### Scenario: Storage failure
- **WHEN** the favorites store cannot be read or written during an add or remove
- **THEN** the caller receives an unavailable result, no partial change is persisted, and no storage detail is disclosed
