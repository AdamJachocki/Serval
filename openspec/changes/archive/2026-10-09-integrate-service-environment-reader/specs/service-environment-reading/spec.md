## Purpose

Provide a complete, bounded read of one system service's supported environment declarations through the application contract, with safe publication and failure behavior.

## ADDED Requirements

### Requirement: Expose one complete declaration read
The library SHALL accept one concrete system service identifier and caller cancellation through the existing application contract. It SHALL return either the complete `SupportedDeclarations` result with canonical identity, ordered value-free source metadata, ordinally sorted variable names and winning source IDs, and separately owned secret values, or a value-free failure. It SHALL reuse the accepted parser and composition semantics without interpreting unit/drop-in contents or adding process-environment sources. Empty declarations SHALL be a successful result. It SHALL NOT change ordinary service inventory behavior.

#### Scenario: Consumer uses only the application contract
- **WHEN** a consumer reads an eligible service with manager assignments and ordered files through the application interface
- **THEN** it receives canonical identity and correct masked metadata/provenance without requiring D-Bus types or implementation paths

#### Scenario: Empty declarations and aliases
- **WHEN** the canonical service has no declarations and is read by its ordinary alias
- **THEN** the read succeeds with canonical identity, empty variable metadata, and the empty manager source

### Requirement: Enforce target policy before sensitive acquisition
Every production entry and construction path SHALL validate the identifier, resolve identity, and enforce canonical/alias and privileged-family protection before querying value-bearing properties or reading source contents. Malformed identifiers and bare templates SHALL be rejected safely. An absent concrete service SHALL return `NotFound`; built-in protected targets SHALL return `ProtectedTarget`, while Serval privileged-family exclusions SHALL preserve the established identity-policy outcome. Calling the library directly SHALL NOT bypass these checks. The capability SHALL remain an internal-use library, without a registered Web or Agent endpoint or a public reveal operation. Future Agent callers MUST authenticate and authorize the exact service operation before invoking it.

#### Scenario: Direct protected alias request
- **WHEN** a caller bypasses any hypothetical Web validation and requests an alias of a protected service
- **THEN** the library denies the read before value-bearing property or content access

#### Scenario: Malicious or excluded identity
- **WHEN** a direct request contains an invalid identifier, a bare template, or an excluded Serval privileged instance
- **THEN** the established identity policy rejects it without exposing environment contents

### Requirement: Validate consistency after computation
Successful publication SHALL follow validation and target protection, acquisition, parsing, computation, final consistency validation, and a final cancellation/deadline check, in that order. Final validation SHALL recheck the relevant manager properties, configuration metadata, retained file-object metadata and path bindings, including continued absence of optional missing files. A detected difference or required property disappearing after resolution SHALL return `InconsistentSnapshot` without retry or partial publication. Transport failures SHALL retain their separate classification. The result SHALL claim only a bounded optimistic observation, not an atomic system snapshot or a future-start guarantee.

#### Scenario: Source changes during parsing or composition
- **WHEN** a source, configuration path, or relevant manager property changes after acquisition while parsing or computation is running
- **THEN** final validation rejects the candidate as `InconsistentSnapshot` and no values are published

#### Scenario: Optional absent file appears
- **WHEN** an optional missing file appears before final validation
- **THEN** the read returns `InconsistentSnapshot` rather than publishing an outdated missing-source model

### Requirement: Preserve one operation budget and cancellation semantics
The complete managed operation SHALL share the accepted five-second budget across identity/protection, connection, source access, parsing, composition, validation and publication. No source or stage SHALL reset that budget. The parser SHALL consume cancellation without introducing its own timer. Caller cancellation SHALL take precedence over deadline expiry and controlled failures and SHALL propagate with the original caller token; observed deadline expiry SHALL return `Timeout`. Cancellation SHALL be checked around non-cancellable synchronous filesystem calls, without claiming to interrupt an already blocked syscall or guarantee a hard five-second wall-clock termination.

#### Scenario: Aggregate duration exhausts the budget
- **WHEN** individually short stages collectively reach the operation deadline
- **THEN** the read returns `Timeout`, including if composition or final validation has already produced a candidate

#### Scenario: Cancellation races a failure or timeout
- **WHEN** caller cancellation is observed before publication alongside a dependency failure or expired deadline
- **THEN** the operation clears unpublished state and throws cancellation carrying the original caller token

#### Scenario: Synchronous syscall returns late
- **WHEN** a synchronous filesystem call returns after cancellation or deadline expiry
- **THEN** any acquired resource is released and the cancellation or timeout is reported before further work or publication

### Requirement: Enforce cumulative parsing allowance
The reader SHALL preserve all accepted resource limits and parse present file occurrences in declaration order using the remaining operation assignment allowance after counting manager entries and all earlier parsed assignments before deduplication. Repeated declarations SHALL consume allowance again. Missing optional occurrences SHALL retain metadata without being parsed. Exactly the accepted bound SHALL be permitted; the first excess SHALL return `LimitExceeded` with the attributable source ID.

#### Scenario: Later file exceeds the remaining allowance
- **WHEN** a later file exceeds the remaining allowance even though its assignments overwrite earlier names
- **THEN** parsing stops with `LimitExceeded` for that occurrence and no partial result is published

### Requirement: Preserve safe failure and ownership boundaries
The reader SHALL preserve the existing failure code, defined unsupported reason, and attributable source ID without raw paths, contents, underlying error text, inner exceptions, or partial metadata/value maps. Parser failure SHALL remain `InvalidSource` or `LimitExceeded`; initial required-file absence SHALL remain `SourceUnavailable`; unsupported configurations SHALL preserve their reason. Every operation-owned unpublished secret buffer and acquired resource SHALL be released on every failure or cancellation exit, including a failure after composition. Only the returned successful result SHALL transfer secret disposal responsibility to the consumer. Value-free metadata SHALL remain usable independently of secret storage, and no new whole-result serializer SHALL be introduced.

#### Scenario: Late failure after secrets were composed
- **WHEN** final validation fails after a complete candidate result exists
- **THEN** the candidate's owned secret buffers are cleared and only a sanitized failure is returned

#### Scenario: A required component fails
- **WHEN** a file is malformed, a required file is initially missing, or a configuration is unsupported
- **THEN** the corresponding existing code/reason/source ID is preserved without previously read values or raw diagnostics

### Requirement: Isolate reads and verify the complete flow
Each read SHALL construct fresh state without value caching, persistent snapshots, or shared mutable per-service state. Concurrent operations and disposal of one result SHALL NOT affect another. The application-facing flow SHALL be verified with synthetic disposable fixtures on the supported systemd 249/255/257/259 CI matrix, without logging generated values or modifying non-test sources.

#### Scenario: Concurrent service reads
- **WHEN** two services with overlapping variable names are read concurrently and one result is disposed
- **THEN** each result retains only its own identity, metadata and values, and the other result remains usable

#### Scenario: Real systemd consumer flow
- **WHEN** the application interface reads disposable fixtures with manager/file conflicts, repeated files, resets, optional absence, aliases and empty declarations
- **THEN** the complete result follows the accepted precedence and provenance while unsupported, missing-required and protected targets fail safely
