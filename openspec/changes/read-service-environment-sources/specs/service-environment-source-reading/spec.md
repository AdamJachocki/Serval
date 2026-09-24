## Purpose

Define a bounded, fail-closed way to resolve one eligible system service and obtain the complete ordered environment-source inputs needed by later composition without exposing arbitrary D-Bus or filesystem access.

## ADDED Requirements

### Requirement: Resolve one concrete eligible service identity
The source reader SHALL accept only a system service identifier and caller cancellation, resolve the identifier through the established system-manager identity mechanism, and verify the canonical identity and every known alias before reading value-bearing properties or source content. It SHALL reject a template without an instance, return `NotFound` for an absent concrete service, return `ProtectedTarget` for a built-in protected service, and reject Serval privileged units and their template-derived instances. Rejection SHALL occur before any environment-file content is opened.

#### Scenario: Resolve an ordinary alias
- **WHEN** an ordinary alias resolves to an eligible concrete canonical service and all returned names are valid and consistent
- **THEN** the reader identifies the snapshot by that canonical service and continues with the manager-declared sources

#### Scenario: Reject protected identity through an alias
- **WHEN** the requested name or any resolved canonical name or alias identifies a protected service or a Serval privileged unit family
- **THEN** the reader returns the applicable controlled denial without opening any environment-file content

#### Scenario: Reject a template without an instance
- **WHEN** the requested identifier names a service template rather than a concrete instance
- **THEN** the reader rejects the request before querying value-bearing properties or opening files

### Requirement: Read only the fixed manager source contract
The source reader SHALL obtain only the fixed manager and service properties required by the accepted environment strategy: canonical identity, known names, load and configuration-consistency metadata, loaded `Environment`, ordered `EnvironmentFiles`, `UnsetEnvironment`, and `PassEnvironment`. It SHALL NOT expose caller-selected D-Bus members, use a broad property read that can return unrelated secrets, read unit or drop-in contents, invoke `systemctl`, scan the filesystem for sources, or reconstruct unit/drop-in selection.

#### Scenario: Obtain manager-processed declarations
- **WHEN** an eligible service has manager-loaded environment assignments and an ordered active environment-file list
- **THEN** the reader obtains those declarations through the fixed typed contract and does not reinterpret unit-file syntax or drop-in ordering

#### Scenario: Reject an invalid manager reply
- **WHEN** a required property is missing, has an incompatible type, exceeds its bound, or conflicts with the resolved identity
- **THEN** the reader returns a controlled value-safe failure and does not treat the missing or invalid property as an empty value

### Requirement: Reject unsupported configuration before file access
The source reader SHALL reject active `UnsetEnvironment` as `UnsupportedConfiguration/UnsetEnvironment`, active `PassEnvironment` as `UnsupportedConfiguration/PassEnvironment`, a transient unit as `UnsupportedConfiguration/TransientUnit`, generated configuration as `UnsupportedConfiguration/GeneratedUnit`, a path pattern as `UnsupportedConfiguration/PathPattern`, an unresolved percent specifier as `UnsupportedConfiguration/UnresolvedSpecifier`, and an unsupported required property as `UnsupportedConfiguration/UnsupportedProperty`. A manager-reported pending daemon reload SHALL produce `InconsistentSnapshot`. Empty `UnsetEnvironment` or `PassEnvironment` lists after reset SHALL be accepted.

#### Scenario: Reject a result-changing unsupported directive
- **WHEN** the resolved service has an active `UnsetEnvironment` or `PassEnvironment` entry
- **THEN** the reader returns the specific unsupported reason before opening any declared environment file and does not substitute an empty environment

#### Scenario: Accept reset-to-empty unsupported directives
- **WHEN** the manager reports empty `UnsetEnvironment` and `PassEnvironment` lists after their directives were reset
- **THEN** the reader continues because no active unsupported directive remains

#### Scenario: Refuse dirty manager state
- **WHEN** the manager reports that the resolved unit needs daemon reload
- **THEN** the reader returns `InconsistentSnapshot` without reloading the manager or retrying indefinitely

### Requirement: Preserve the complete ordered source set
On success, the source reader SHALL return the decoded manager `Environment` contribution as request-local source ID 0 and every active `EnvironmentFiles` occurrence as a distinct source ID 1 through N in manager order. It SHALL preserve repeated paths and optionality, include an optional initially absent file as missing source metadata, and return the successfully read content for every present occurrence. It SHALL either return this complete source set or a failure; it SHALL NOT publish a partial success or calculate cross-source winning values.

#### Scenario: Preserve order, repetition, and optionality
- **WHEN** the manager returns multiple file declarations including a repeated path and an optional declaration
- **THEN** each occurrence has a distinct consecutive source ID in manager order with its own optional or missing state

#### Scenario: Return an empty complete source set
- **WHEN** the manager contribution is empty and no active environment files remain after reset
- **THEN** the reader returns a successful complete snapshot containing the empty manager source and no file occurrences

#### Scenario: Discard a partial candidate
- **WHEN** any required source or final consistency check fails after earlier sources were read
- **THEN** the reader clears and discards the complete unpublished candidate and returns only the controlled failure

### Requirement: Distinguish optional absence from source failure
The source reader SHALL treat only initial not-found for an optional environment file as a legal missing source. Initial not-found for a required file SHALL return `SourceUnavailable`. Permission failure, I/O failure, unsafe file type, or any other error SHALL NOT be converted into optional absence. A source that disappears or becomes unavailable after its initial observation SHALL return `InconsistentSnapshot`.

#### Scenario: Accept an initially absent optional file
- **WHEN** an optional declared path is absent at its initial safe lookup and remains absent through final validation
- **THEN** the reader records that source occurrence as optional and missing without fabricating content

#### Scenario: Reject an absent required file
- **WHEN** a required declared path is absent at initial lookup
- **THEN** the reader returns `SourceUnavailable` for that source ID

#### Scenario: Do not hide an optional access error
- **WHEN** an optional declared path exists but cannot be read for a reason other than initial not-found
- **THEN** the reader returns the applicable controlled failure instead of marking the source missing

### Requirement: Open declared paths safely without arbitrary file access
The source reader SHALL accept file paths only from the validated manager declaration, require bounded absolute unambiguous paths, and SHALL NOT accept a caller-supplied path. It SHALL resolve each path without following symbolic links in any component, require a regular final file, and refuse FIFO, socket, device, process pseudo-file, and other unsafe objects without blocking on their content. It SHALL perform no source-file mutation.

#### Scenario: Read a regular administrator-managed source outside Serval directories
- **WHEN** the manager declares a stable regular environment file at a legal absolute path outside `/etc/serval`
- **THEN** the reader may read it through the safe-open policy because declared administrator sources are not limited to Serval-owned directories

#### Scenario: Reject a symbolic link
- **WHEN** a declared path contains an intermediate symbolic link or its final component is a symbolic link
- **THEN** the reader returns `UnsupportedConfiguration/UnsafePath` without following the link

#### Scenario: Reject a special or pseudo file
- **WHEN** a declared source resolves to a FIFO, socket, device, or process pseudo-file
- **THEN** the reader rejects it without consuming its content or waiting indefinitely

### Requirement: Detect inconsistent observations
The source reader SHALL capture and later revalidate the relevant manager properties, canonical identity, fragment and drop-in metadata, open file-object identity and metadata, and path-to-object bindings. It SHALL return `InconsistentSnapshot` without unbounded retry when it detects source replacement, mutation during reading, configuration-path change, service disappearance, or any other relevant difference. Success SHALL represent a bounded optimistic observation only and SHALL NOT claim an atomic system-wide snapshot or guarantee the sources used by a future service start.

#### Scenario: Detect path substitution
- **WHEN** a declared path resolves to a different object between initial opening and final validation
- **THEN** the reader returns `InconsistentSnapshot` and publishes no source content

#### Scenario: Detect content change during reading
- **WHEN** relevant metadata of an opened source changes while its content is being read
- **THEN** the reader returns `InconsistentSnapshot` and clears the unpublished content

#### Scenario: Return a stable optimistic observation
- **WHEN** all manager, configuration-path, open-object, and path-binding observations remain consistent through final validation
- **THEN** the reader may publish the complete source snapshot while making no future-start or global atomicity guarantee

### Requirement: Enforce fixed bounds, managed deadline, and cancellation
The source reader SHALL enforce the accepted inclusive M2 limits observable during acquisition: at most 65 total sources including the manager source, 1,048,576 bytes per source, 4,194,304 bytes across all source occurrences, 16,384 entries in the decoded manager contribution, 4,096 UTF-8 bytes per path, 129 configuration paths, and one five-second managed-operation deadline. Repeated file declarations SHALL count separately. File-assignment accounting that requires parsing file content is outside this acquisition capability. Equality SHALL be accepted when otherwise valid and the next byte or item SHALL return `LimitExceeded`. Caller cancellation SHALL remain caller cancellation and take precedence when already requested; observed deadline expiry SHALL return `Timeout`.

The reader SHALL pass cancellation to asynchronous operations and SHALL check cancellation immediately before and after each synchronous Linux filesystem call. Because `open`, `openat2`, `statx`, and `fstatfs` provide no cancellation or deadline parameter, the reader SHALL NOT claim that the token interrupts an in-progress syscall or that the five-second deadline is a strict wall-clock bound while such a syscall is blocked. If a synchronous call returns after cancellation or deadline expiry, the reader SHALL discard its result, release any acquired resource, clear unpublished secret state, and report caller cancellation or `Timeout` before continuing or publishing success.

#### Scenario: Accept exact limits
- **WHEN** a stable valid source set is exactly at every applicable inclusive bound and completes before the deadline
- **THEN** the reader may return the complete snapshot

#### Scenario: Reject the first excess item
- **WHEN** source count, source bytes, total bytes, manager-entry count, path bytes, or configuration-path count exceeds its limit by one
- **THEN** the reader returns `LimitExceeded` without partial source data

#### Scenario: Preserve caller cancellation
- **WHEN** caller cancellation is observed before publication, including while file I/O is pending
- **THEN** the reader clears unpublished secret state and throws cancellation associated with the caller token rather than returning `Timeout`

#### Scenario: Observe cancellation around a synchronous filesystem call
- **WHEN** caller cancellation or the managed deadline expires while a non-cancellable Linux filesystem syscall is in progress and that syscall later returns
- **THEN** the reader releases any result of the syscall and reports the observed cancellation or `Timeout` without performing the next acquisition step or publishing source content

### Requirement: Keep source values and diagnostics confidential
The source reader SHALL treat manager entries and file bytes as secrets from first receipt. Results used for downstream composition SHALL not expose paths, raw declarations, values, value lengths, source snippets, raw D-Bus errors, raw filesystem errors, or inner exceptions through public metadata, failure objects, serialization, string representations, logs, telemetry, audit data, snapshots, or test diagnostics. Failures SHALL expose only the accepted failure code, optional defined unsupported reason, and optional request-local source ID. Unpublished or losing owned buffers SHALL be cleared promptly.

#### Scenario: Fail after reading secret content
- **WHEN** a later source or final consistency check fails after secret content was obtained
- **THEN** the failure contains only safe classification metadata and the previously obtained content is cleared rather than published or logged

#### Scenario: Sanitize untrusted path and system errors
- **WHEN** validation or I/O fails for a path or error supplied by an external system
- **THEN** diagnostics omit the raw path, payload, and operating-system error text

### Requirement: Verify manager semantics on supported real-systemd baselines
The capability SHALL be verified against the supported systemd 249, 255, 257, and 259 Linux matrix using collision-checked disposable concrete services. Verification SHALL cover base and drop-in configuration, a concrete instance, aliases, a runtime source, optional and required absence, an unsupported construction, and source ordering without modifying any non-test source or exposing generated values.

#### Scenario: Observe ordered manager sources on real systemd
- **WHEN** a disposable concrete service uses a base fragment, ordered drop-ins, a reset, an instance expansion, and a runtime environment-file source
- **THEN** the reader observes the canonical identity and final active source order reported by that supported systemd baseline without reparsing unit contents

#### Scenario: Fail closed on a real unsupported construction
- **WHEN** a disposable service has a manager-observed construction that the M2 strategy rejects
- **THEN** the reader returns the explicit controlled failure rather than a successful empty or partial source set
