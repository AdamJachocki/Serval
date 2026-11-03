## Why

Serval can decode manager-loaded `Environment=` entries and parse one environment file, but it cannot yet resolve a concrete system service and safely collect the complete ordered set of supported environment sources. Issue #38 closes that gap so later composition can operate on a bounded, provenance-preserving, fail-closed snapshot without exposing a generic file-read or D-Bus capability.

## What Changes

- Add an internal read-only `Serval.Systemd` source reader that resolves one concrete system service through the established M1 identity mechanism and rejects templates, protected services, and Serval privileged units before any value-bearing property or file read.
- Extend the internal typed systemd D-Bus boundary only with the fixed Unit and Service properties required by the accepted M2 environment strategy; do not add caller-selected properties, `GetAll`, or generic D-Bus access.
- Collect manager-loaded `Environment=` entries and the ordered active `EnvironmentFiles` declarations, preserving repetitions and optionality, while rejecting active `UnsetEnvironment`, active `PassEnvironment`, dirty, transient, generated, malformed, or otherwise unsupported configurations with explicit safe failures.
- Validate fragment, drop-in, and environment-file paths as untrusted data. Read only manager-declared environment files using bounded, no-follow, regular-file-only access that rejects special files and detectable path substitution or content changes.
- Publish a complete internal source snapshot only after identity, configuration metadata, open-object metadata, and path bindings remain consistent across final validation. Return `InconsistentSnapshot` without unbounded retry when detectable changes occur.
- Apply the fixed M2 source-count, byte, manager-entry, path, and configuration-path limits plus the five-second managed-operation deadline while acquiring sources. Cancellation is cooperative for asynchronous work and is checked around synchronous Linux filesystem calls; the deadline does not claim to interrupt a kernel syscall or provide a strict wall-clock bound while such a call is blocked. Keep source contents, paths, raw filesystem errors, and D-Bus payloads out of diagnostics. File-assignment accounting remains with later parsing and composition.
- Add focused unit, Linux filesystem, and real-systemd coverage for success, denial, malformed replies, optional and required absence, aliases and instances, ordering, unsafe files, races, cancellation, timeout, and resource limits.
- Keep multi-source value composition, the `ISystemServiceEnvironmentReader` adapter, IPC, authorization, Web exposure, persistence, writes, daemon reload, and service lifecycle outside this change.

## Capabilities

### New Capabilities

- `service-environment-source-reading`: Resolve an eligible concrete system service and return either its complete, ordered supported environment-source snapshot or a controlled value-safe failure.

### Modified Capabilities

None. The existing `environment-file-parsing` contract is consumed without changing its requirements.

## Impact

- `Serval.Systemd`: new reader/orchestration and safe-file abstractions; narrowly expanded D-Bus protocol, proxies, transport validation, and source-snapshot logic.
- `Serval.Application`: existing failure classifications and secret-owning environment types may be consumed internally, but the `ISystemServiceEnvironmentReader` contract is not implemented or changed by this issue.
- Tests and fixtures: focused systemd adapter tests plus disposable real-systemd fixtures across the supported Linux/systemd matrix.
- Security boundary: introduces a library-level capability to read manager-declared service environment sources, potentially including root-readable files, but does not activate Serval.Agent or expose the capability through IPC or Web. Future Agent exposure still requires authenticated identity, resource authorization, protected-target enforcement, and value-safe auditing before invoking it.
- No new package, project, persistence schema, endpoint, write capability, reload, or restart behavior is introduced.
