# Spec Delta

## MODIFIED Requirements

### Requirement: Per-service permission grants
Serval SHALL support grants of the `Service.View`, `Environment.View`, and `Environment.Reveal` operations on a concrete canonical system service to a Linux user or group. Apart from the configured host-administrator group, a subject without a matching grant SHALL have no access by default. Permissions SHALL be evaluated against the caller's current Linux identity and group membership for each request; submitted identity or membership fields SHALL have no authority.

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
Installation SHALL select an existing Linux host-administrator group as Serval's initial administrator subject. Current members of that group SHALL be able to manage Serval grants, view non-protected services, inspect their environment metadata, and reveal individual environment values without separate grants or a separate Serval bootstrap account. Only such administrators SHALL be able to list, create, or remove grants; grant management SHALL accept only supported operations, existing Linux user or group subjects, and concrete canonical service identities.

#### Scenario: Initial administrator
- **WHEN** an authenticated user currently belongs to the configured host-administrator group
- **THEN** Agent permits that user to manage grants, view non-protected services, inspect their environment metadata, and reveal individual values

#### Scenario: Unauthorized grant change
- **WHEN** a user outside the configured host-administrator group requests a grant change, even with an asserted administrator flag
- **THEN** Agent rejects the change and leaves the policy unchanged

#### Scenario: Invalid grant target
- **WHEN** an administrator submits an alias, template, protected service, nonexistent subject, unsupported operation, or malformed service identity as a grant
- **THEN** Agent rejects the grant without changing policy


## ADDED Requirements

### Requirement: Independent environment permissions
Agent SHALL require `Environment.View` for environment metadata and both `Environment.View` and `Environment.Reveal` for value disclosure on the canonical service. `Service.View` SHALL NOT imply either environment permission. Grants SHALL use existing current identity and group matching. Current host administrators SHALL receive both permissions for non-protected services; protected-service policy SHALL always take precedence.

#### Scenario: View without reveal
- **WHEN** a user has environment view permission but not reveal permission
- **THEN** metadata is allowed and disclosure is denied before sensitive acquisition for that request

#### Scenario: Reveal without view
- **WHEN** a user has only an environment reveal grant
- **THEN** both listing and disclosure are denied

#### Scenario: Service permission alone
- **WHEN** a user has only `Service.View`
- **THEN** neither environment metadata nor values are released

#### Scenario: Protected administrator request
- **WHEN** a current host administrator requests a protected canonical service or its alias
- **THEN** both environment operations are denied before sensitive acquisition

#### Scenario: Removed membership
- **WHEN** a user loses a qualifying grant or current Linux group membership
- **THEN** the next environment request is evaluated without that permission

### Requirement: Environment grant administration
Administrators SHALL be able to create, list and remove grants for both environment operations using the existing subject and canonical-service validation. Existing `Service.View` grants SHALL retain their meaning without automatic environment grants. Granting `Environment.Reveal` SHALL NOT implicitly grant `Environment.View`; the administration interface SHALL explain the dependency.

#### Scenario: Grant operations independently
- **WHEN** an administrator grants either environment operation
- **THEN** only that operation is stored and visible in the grant list
- **AND** removal does not remove or create other grants

#### Scenario: Invalid or unauthorized grant management
- **WHEN** a non-administrator attempts a grant change or an administrator supplies an invalid subject, alias, protected target or unsupported operation
- **THEN** the request is rejected without changing grants

### Requirement: Audited environment disclosure
Agent SHALL audit environment decisions using verified actor, operation, canonical service when known, outcome and correlation metadata. Disclosure records SHALL include the validated requested variable name, never values, lengths, hashes, source paths or session credentials. Failure to record the audit SHALL prevent disclosure. Existing audit records SHALL survive the compatible schema migration.

#### Scenario: Successful reveal
- **WHEN** a permitted disclosure is ready
- **THEN** its audit record is persisted before releasing the value

#### Scenario: Failed or denied reveal
- **WHEN** disclosure is denied or unavailable
- **THEN** a bounded value-free audit is attempted and no value is released
- **AND** malformed names are omitted rather than copied into audit records

#### Scenario: Audit storage unavailable
- **WHEN** the audit write fails
- **THEN** the request fails closed without releasing the candidate value
