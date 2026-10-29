# Service Environment Specification

## Purpose

Allow authorized operators to inspect a service's supported environment declarations and deliberately reveal individual values without persisting secrets.

## Requirements

### Requirement: Masked environment presentation
Service details SHALL provide a Polish environment section for users with `Service.View` and `Environment.View`. It SHALL show ordinally sorted names, constant-length masks and winning-source labels. Initial HTML and metadata responses SHALL contain no values. The section SHALL identify its scope as supported configuration declarations, not the running process environment.

#### Scenario: Metadata loaded
- **WHEN** an authorized user opens service details
- **THEN** the section shows names and source labels with every value masked, including empty values
- **AND** source labels distinguish aggregate systemd declarations from numbered environment-file occurrences without exposing paths or claiming unit-line provenance

#### Scenario: Only service access
- **WHEN** a user has `Service.View` but lacks `Environment.View`
- **THEN** service details remain usable without environment names, sources or values

### Requirement: Explicit individual disclosure
The UI SHALL offer `Pokaż` only when Agent-authorized metadata indicates reveal permission. Clicking it SHALL request only that variable's current value through an anti-forgery-protected operation. Values SHALL be rendered as text, including multiline and empty values. No bulk reveal or automatic value prefetch SHALL occur.

#### Scenario: Reveal one value
- **WHEN** a permitted user selects `Pokaż` on one row
- **THEN** only that row receives a current authorized value and an `Ukryj` button
- **AND** an empty value is identified as empty only after successful disclosure

#### Scenario: Metadata-only permission
- **WHEN** a user can view environment metadata but cannot reveal values
- **THEN** rows remain masked and no usable reveal control is offered

#### Scenario: Untrusted content
- **WHEN** a name or disclosed value contains markup-like characters or line breaks
- **THEN** it is displayed as inert text without executing markup or scripts

### Requirement: User-controlled hiding
A disclosed value SHALL remain visible without a time-based hiding timer. `Ukryj` SHALL remove the value from the row and retained client state. Page refresh or navigation SHALL reset all rows to masked state; values SHALL NOT be restored from browser history or browser storage. Revealing again SHALL require a new authorized request.

#### Scenario: No timeout
- **WHEN** a successfully disclosed row remains open without user action
- **THEN** elapsed time alone does not hide its value

#### Scenario: Hide and reveal again
- **WHEN** the user selects `Ukryj` and later `Pokaż`
- **THEN** the previous value is discarded and a new request is made rather than using a cached value

#### Scenario: Refresh and history restoration
- **WHEN** the page is refreshed or restored through browser history
- **THEN** every value is masked and fresh authorization is required before disclosure

#### Scenario: Hide while disclosure is pending
- **WHEN** refresh, navigation or hiding invalidates a pending disclosure
- **THEN** its late response cannot repopulate a masked row or a different service page

### Requirement: Failure and confidentiality presentation
Environment responses SHALL use no-store caching and SHALL NOT place values in URLs, cookies, browser storage, logs or telemetry. The section SHALL distinguish empty success from unavailable, unsupported or inconsistent reads. A failed reveal SHALL leave its row masked without stale fallback. Authentication failure SHALL follow existing session invalidation behavior.

#### Scenario: Empty declarations
- **WHEN** a complete read succeeds with no declarations
- **THEN** the section displays an explicit empty state

#### Scenario: Read fails
- **WHEN** an environment read fails, exceeds limits or detects changing configuration
- **THEN** the section shows a sanitized failure and retry control without partial rows or values
- **AND** independently authorized service-state details remain usable

#### Scenario: Permission revoked or variable removed
- **WHEN** disclosure is requested after access has been revoked or the variable has disappeared
- **THEN** no value is returned and that row remains masked with a sanitized error
