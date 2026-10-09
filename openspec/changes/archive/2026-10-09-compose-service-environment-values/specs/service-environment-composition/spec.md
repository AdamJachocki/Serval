## Purpose

Define deterministic composition of a complete ordered set of supported systemd environment declarations into effective variable values and winning-source metadata, without exposing their contents through diagnostics.

## ADDED Requirements

### Requirement: Compose only a complete ordered declaration set
The composition capability SHALL accept one decoded manager `Environment` contribution as source ID 0 and one ordered occurrence for every active manager-declared `EnvironmentFile`, with consecutive request-local IDs 1 through N. Each present file occurrence SHALL have a corresponding successfully parsed contribution; each missing occurrence SHALL be an explicitly optional source with no assignments. A repeated file declaration SHALL remain a distinct occurrence. An incomplete, mismatched, unordered, or otherwise invalid set SHALL NOT produce a partial success. The composer SHALL perform no D-Bus or filesystem I/O.

#### Scenario: Complete repeated declarations
- **WHEN** the ordered input contains two present occurrences of the same declared file with distinct consecutive IDs
- **THEN** both occurrences participate separately in composition and remain separate source metadata entries

#### Scenario: Optional source is absent
- **WHEN** an optional file occurrence is explicitly marked missing between two present occurrences
- **THEN** the result retains its missing source metadata and applies no assignments from that occurrence

#### Scenario: Required contribution is omitted
- **WHEN** a declared present occurrence lacks its parsed contribution, a required occurrence is marked missing, or occurrence IDs/order do not match the declaration set
- **THEN** composition rejects the input without publishing a partial value map

### Requirement: Apply systemd source precedence to supported declarations
Composition SHALL begin with the manager-loaded `Environment` contribution and then apply every present file contribution in manager declaration order. A file assignment SHALL override a manager assignment with the same name, and a later file occurrence SHALL override an earlier file occurrence. Within one contribution, its already decoded or parsed last assignment SHALL be used. Names SHALL be compared with ordinal, case-sensitive equality. An explicitly empty winning value SHALL remain present and SHALL NOT be treated as absence. The composer SHALL NOT replay unit/drop-in syntax, resets, specifiers, or process environment additions.

#### Scenario: File overrides manager
- **WHEN** source 0 and source 1 both contain the same variable name
- **THEN** the result contains source 1's value and winning source ID 1

#### Scenario: Later file overrides earlier file
- **WHEN** two present file occurrences contain the same variable name
- **THEN** the result contains the later occurrence's value and its distinct source ID

#### Scenario: Empty and case-distinct variables
- **WHEN** a later contribution assigns an empty value to `NAME` and another contribution assigns `name`
- **THEN** `NAME` remains present with an empty value and `name` remains a separate variable

### Requirement: Publish bounded value-free provenance
The successful supported-declarations result SHALL contain the canonical service identity, source metadata in declaration order, and variable metadata sorted by ordinal name with exactly the winning source ID for each name. Source and variable metadata SHALL NOT contain values, value lengths or hashes, paths, unit-file locations, reset history, or losing assignments. No variable SHALL cite a missing or unknown source.

#### Scenario: Deterministic result for equivalent ordered inputs
- **WHEN** the same canonical identity and ordered contributions are composed repeatedly
- **THEN** the variable values, winning source IDs, source metadata order, and variable metadata order are identical

#### Scenario: Manager provenance remains aggregate
- **WHEN** a variable wins from the manager-loaded contribution, including after a unit/drop-in reset already resolved by systemd
- **THEN** its winning source ID is 0 and no original unit line or reset location is claimed

### Requirement: Enforce operation-wide composition bounds
Composition SHALL enforce the accepted inclusive maximum of 65 total source occurrences, 4,194,304 total source bytes counting repeated occurrences separately, and 16,384 manager-plus-file assignments before deduplication or cross-source overrides. The first excess item SHALL cause a value-free `LimitExceeded` outcome with the offending source ID when attributable to a source. A supplied file contribution whose recorded count exceeds the remaining assignment allowance SHALL NOT be accepted even if its retained variable map is small.

#### Scenario: Exact assignment limit
- **WHEN** the manager and ordered file contributions report exactly 16,384 assignments in total and otherwise satisfy the limits
- **THEN** composition may succeed even when many assignments were overridden

#### Scenario: Overridden assignment exceeds limit
- **WHEN** the next file occurrence makes the operation count 16,385, including an assignment that loses to a later one
- **THEN** composition returns `LimitExceeded` without publishing values

#### Scenario: Repeated source consumes byte allowance
- **WHEN** a repeated file occurrence makes the aggregate source-byte count exceed 4,194,304
- **THEN** composition returns `LimitExceeded` even if the same path was read earlier

### Requirement: Preserve secret ownership and cancellation
All candidate and winning values SHALL remain in disposable, non-public value storage. Losing values and every unpublished candidate SHALL be cleared promptly on replacement, invalid input, limit failure, cancellation, or result-construction failure. Successful publication SHALL transfer disposal responsibility to the result consumer. Composition SHALL honor caller cancellation before work, during bounded work, and immediately before publication; observed caller cancellation SHALL throw `OperationCanceledException` associated with the caller token and SHALL NOT be converted to a result failure. Results, exceptions, serialization, string representations, diagnostics, logs, and test output SHALL NOT reveal any environment value.

#### Scenario: Later source replaces a secret
- **WHEN** a later present file occurrence overrides an earlier secret value
- **THEN** only the winning value remains owned by the published result and the losing owned buffer is cleared

#### Scenario: Failure after earlier contributions
- **WHEN** an invalid or over-limit later contribution is encountered after earlier secret contributions
- **THEN** all unpublished candidate buffers are cleared and the failure exposes no values or source content

#### Scenario: Cancellation before publication
- **WHEN** caller cancellation is observed after composition has processed a contribution but before success is published
- **THEN** unpublished buffers are cleared and cancellation with the caller token is thrown

### Requirement: Verify precedence against supported systemd baselines
The accepted source-order and precedence behavior SHALL be verified using disposable synthetic fixtures against real systemd 249, 255, 257, and 259 baselines. Verification SHALL include manager versus file conflicts, multiple files, repeated file declarations, empty values, optional missing files, and unit/drop-in resets as observed through the manager's final properties. Tests SHALL NOT modify non-test sources or print generated values.

#### Scenario: Compare composed and manager-observed winners
- **WHEN** a disposable service supplies conflicting supported declarations and the same complete ordered inputs are composed by Serval
- **THEN** Serval's winning values agree with the manager-observed result and its source IDs identify the corresponding aggregate manager or file occurrence without claiming unit-line provenance
