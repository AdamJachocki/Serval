## Why

Serval can decode the manager's loaded `Environment=` contribution, parse individual `EnvironmentFile=` contents, and acquire an ordered source snapshot, but it cannot yet calculate the supported effective value and provenance for each variable. Issue #39 adds the deterministic, I/O-free composition boundary needed before the application-facing reader in #40 can join those pieces.

## What Changes

- Add an internal `Serval.Systemd` composer that consumes one complete, ordered set of already decoded manager and parsed file contributions, including legally missing optional occurrences.
- Apply the accepted systemd precedence: manager contribution first, then every active file occurrence in manager order. Preserve case-sensitive ordinal names, empty values, repeated file occurrences, and the winning request-local source ID.
- Produce the M2.1 supported-declarations result with source metadata in declaration order and variable metadata sorted by ordinal name. Do not invent unit-line provenance or retain overridden values.
- Reject incomplete, misordered, mismatched, or over-limit input without publishing a partial result. Enforce the operation-wide assignment allowance using counts from decoded and parsed contributions.
- Keep secret values in owned, disposable buffers; clear losing and unpublished buffers and keep values out of metadata, diagnostics, serialization, and tests.
- Add focused unit tests for precedence, provenance, limits, malformed input, cancellation, deterministic ordering, and buffer disposal, plus real-systemd precedence evidence using disposable synthetic fixtures.

This change does not read files or D-Bus, resolve services, implement `ISystemServiceEnvironmentReader`, expose IPC or Web endpoints, authorize users, persist values, write configuration, reload systemd, or control service lifecycle. Issue #40 will parse acquired file bytes with the decreasing remaining assignment allowance and connect acquisition to this composer.

## Capabilities

### New Capabilities

- `service-environment-composition`: Compose complete ordered manager and parsed file contributions into a bounded, provenance-preserving supported-declarations result.

### Modified Capabilities

None. The accepted `environment-file-parsing` requirements remain unchanged.

## Impact

- `Serval.Systemd`: internal composition and input-ownership boundary, consuming the existing manager decoder, file parser results, source occurrence metadata, and fixed M2 limits.
- `Serval.Application`: reuse the existing `ServiceEnvironmentReadResult`, `EnvironmentValues`, and metadata contracts without changing their public behavior.
- Tests: focused managed tests and supported real-systemd precedence fixtures; no new runtime consumer, package, project, database schema, root operation, or trust-boundary exposure.
- Security: values remain secret in memory and results. The future Agent must independently authenticate, authorize, and enforce protected-service policy before any value-bearing read; this change grants no such access.
