## Why

Serval can decode systemd's already-loaded `Environment=` property, but it cannot yet interpret the current contents of declared `EnvironmentFile=` sources. A bounded, non-executing parser is needed before the systemd adapter can safely compose those files into the supported-declarations environment model.

## What Changes

- Add an I/O-free parser for raw `EnvironmentFile=` bytes that follows the version-independent subset of systemd's documented UTF-8, comment, whitespace, quoting, escaping, continuation, and multiline rules across the supported 249/255/257/259 baselines.
- Preserve literal values without interpolation, command execution, specifier expansion, or dotenv/`export` behavior.
- Validate variable names, encoding, forbidden Unicode scalars, NUL handling, assignment syntax, terminated quoting, and version-divergent comment continuations using single-byte LF or CR endings, returning only value-free failure information.
- Enforce the existing per-source, logical-record, and assignment limits while accounting for overridden assignments.
- Replace duplicate assignments within one file with the last value, clear losing secret buffers promptly, and attribute winning variables to the supplied request-local source ID.
- Verify behavior with managed boundary/negative-path tests and real-systemd parser parity fixtures for supported Debian and Ubuntu baselines.

## Capabilities

### New Capabilities

- `environment-file-parsing`: Parse one bounded systemd `EnvironmentFile=` content source into secret values and value-free provenance metadata without executing or expanding its contents.

### Modified Capabilities

None.

## Impact

- Affects `Serval.Systemd` parsing and its internal candidate-result composition contracts.
- Reuses `EnvironmentValues`, `EnvironmentVariableMetadata`, and `EnvironmentReadFailureCode` from `Serval.Application` without exposing values through public metadata, logs, or serialization; declaration-level source metadata remains the future composer's responsibility.
- Adds unit coverage in `Serval.Systemd.Tests` and real-systemd fixture coverage; it does not add file access, Web/Agent registration, IPC, authorization, writes, daemon reload, or service lifecycle behavior.
