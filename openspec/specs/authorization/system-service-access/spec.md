# System Service Access Specification

## Purpose

Control each user's access to system services with Agent-enforced permissions tied to Linux identities and canonical service names.

## Requirements

### Requirement: Per-service permission grants
Serval SHALL support grants of the `Service.View` operation on a concrete canonical system service to a Linux user or group. Apart from the configured host-administrator group, a subject without a matching grant SHALL have no access by default. Permissions SHALL be evaluated against the caller's current Linux identity and group membership for each request; submitted identity or membership fields SHALL have no authority.

#### Scenario: User grant
- **WHEN** a current Linux user has a `Service.View` grant for the requested canonical service
- **THEN** Agent permits that service's read-only metadata if protected policy also permits it

#### Scenario: Group grant
- **WHEN** a current Linux user belongs to a Linux group with a matching grant
- **THEN** Agent permits that service's read-only metadata if protected policy also permits it

#### Scenario: No grant or removed membership
- **WHEN** a user has no matching grant or is no longer a member of the granted group
- **THEN** Agent denies access to the service

### Requirement: Host-administrator bootstrap and grant management
Installation SHALL select an existing Linux host-administrator group as Serval's initial administrator subject. Current members of that group SHALL be able to manage Serval grants and view non-protected services without a separate Serval bootstrap account. Only such administrators SHALL be able to list, create, or remove grants; grant management SHALL accept only supported operations, existing Linux user or group subjects, and concrete canonical service identities.

#### Scenario: Initial administrator
- **WHEN** an authenticated user currently belongs to the configured host-administrator group
- **THEN** Agent permits that user to manage grants and view non-protected services

#### Scenario: Unauthorized grant change
- **WHEN** a user outside the configured host-administrator group requests a grant change, even with an asserted administrator flag
- **THEN** Agent rejects the change and leaves the policy unchanged

#### Scenario: Invalid grant target
- **WHEN** an administrator submits an alias, template, protected service, nonexistent subject, unsupported operation, or malformed service identity as a grant
- **THEN** Agent rejects the grant without changing policy

### Requirement: Stable subject and service matching
Agent SHALL bind grants to both a Linux subject name and its resolved numeric identity so a reused name or numeric ID alone does not inherit a grant. Agent SHALL resolve service aliases to their canonical concrete identity before permission evaluation and SHALL compare full, case-sensitive, escaped service names.

#### Scenario: Reused Linux identity
- **WHEN** a granted Linux username or group name resolves to a different numeric identity than at grant creation
- **THEN** Agent ignores that grant until an administrator deliberately replaces it

#### Scenario: Alias inspection
- **WHEN** a user inspects an alias of a service for which they have a canonical grant
- **THEN** Agent applies the grant to the resolved canonical identity and returns that canonical identity

#### Scenario: Distinct instances
- **WHEN** a user has a grant for one concrete service instance but requests another case-distinct or differently escaped instance
- **THEN** Agent does not treat the first grant as a match

### Requirement: Protected-service precedence
Agent SHALL apply the protected-service policy to the canonical identity and every validated alias before releasing service metadata. In this change, built-in protected services SHALL be hidden and denied even to Serval administrators; Serval's own privileged units SHALL remain absent from normal discovery and inspection. Ordinary grants SHALL NOT override either rule.

#### Scenario: Protected alias
- **WHEN** an ordinary-looking alias resolves to a built-in protected service
- **THEN** Agent does not return that service even when the user has a grant for the alias or canonical name

#### Scenario: Privileged Serval unit
- **WHEN** a user requests a Serval Agent unit or its alias
- **THEN** it remains absent regardless of administrator status or grants

### Requirement: Policy failure and audit
Agent SHALL deny a protected operation if identity, group, grant, or protected-service policy cannot be evaluated reliably. Agent SHALL record bounded audit metadata for grant changes and service-access decisions, including verified actor, operation, canonical service when known, outcome, and correlation identifier, without credentials or environment values.

#### Scenario: Policy store unavailable
- **WHEN** Agent cannot read policy needed for a service decision
- **THEN** Agent denies the operation and does not return service metadata

#### Scenario: Denied access audit
- **WHEN** Agent denies an attempted service inspection
- **THEN** it records the verified actor and sanitized decision metadata without recording a password, session credential, or service environment value
