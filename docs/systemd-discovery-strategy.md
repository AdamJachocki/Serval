# Systemd service discovery strategy

Status: Accepted  
Date: 2026-09-02  
Issue: [#8](https://github.com/AdamJachocki/Serval/issues/8)

## Context

Serval needs a complete, read-only inventory of system-level service units. The inventory must include installed but inactive services and loaded runtime units, retain systemd's load, active, and sub-state values, and identify one canonical name when aliases are present. Discovery must also distinguish template definitions from concrete instances.

The application-facing contract is `ISystemServiceInventory`. Systemd transport details stay in `Serval.Systemd` and must not leak into `Serval.Application`.

## Decision

The adapter will use the system manager's `org.freedesktop.systemd1` D-Bus API directly. It will not execute `systemctl` for discovery.

The adapter connects only to the system bus and the system manager object at `/org/freedesktop/systemd1`. It never connects to a per-user manager. The future runtime owner is `serval-agent`; `serval-web` must not reference, host, or invoke the adapter directly. Web reaches the inventory only through authenticated, narrow local IPC to Agent.

Direct D-Bus was chosen because it provides typed, locale-independent replies and canonical unit object identities without starting a child process or parsing terminal-oriented output. It also avoids adding a generic process-launch capability to the privileged process. Direct `systemctl` execution remains a rejected fallback: the implementation must fail as unsupported if the required D-Bus API is unavailable rather than silently changing transport.

No executable is used by this decision. If a later decision introduces a process for any systemd operation, it must name a fixed executable and pass fixed options and validated unit arguments separately through a process API, without a shell or command-string construction.

## Discovery algorithm

One inventory operation builds a new snapshot and publishes it only after every required step succeeds:

1. Read the manager's `Version` property and reject systemd versions below the supported baseline.
2. Call `ListUnitFiles` and retain entries whose validated basename is a `.service` unit. This is the source for installed units, including inactive units, aliases, template definitions, and explicitly installed instances.
3. Call `ListUnitsByPatterns` with the name pattern `*.service` and no state restriction. This is the source for all service units currently loaded by PID 1, including inactive loaded units, transient or generated services, and instantiated template units that have no instance file on disk.
4. Call `ListUnitsByNames` for the concrete installed names not already described by the loaded-unit result. This returns runtime state for inactive concrete services without starting them.
5. Read `Id` and `Names` from each distinct `org.freedesktop.systemd1.Unit` object. `Id` is the canonical `SystemServiceId`; `Names` supplies the aliases. Merge entries by canonical, case-sensitive `Id`, not by the originally discovered name or D-Bus object path.
6. Map `Description`, `LoadState`, `ActiveState`, and `SubState` into the application read model. Preserve unknown state strings so a newer systemd state does not become a data-loss or availability failure.
7. Apply Agent-side protected-service and authorization policy to the canonical ID before returning any record. Serval's own privileged units are always excluded. An alias cannot bypass policy because authorization and protection checks use the resolved canonical ID and all known names.
8. Sort the final snapshot by canonical unit ID using ordinal comparison to make results deterministic.

The adapter treats D-Bus results as untrusted input at its boundary. Every returned name must pass the same bounded system service unit-name validation used for caller-supplied identifiers before it is used in another D-Bus request or returned to an application caller.

### Inactive services

`ListUnitFiles` supplies services that are installed but not loaded. `ListUnitsByNames` supplies their current systemd properties. Discovery does not start, stop, reload, enable, or otherwise change a service. An installed unit that disappears during the snapshot is treated as a discovery race and omitted after one bounded re-resolution; other failures fail the snapshot.

### Aliases

An alias reported by the unit-file list is resolved through the unit object. The unit object's `Id` is the primary name and its `Names` property contains the primary name and aliases. Only one inventory item is returned for that object. Inspecting by an alias may resolve to the canonical item, but validation, `Service.View` authorization, protected-service policy, and audit metadata all use the canonical identity and cannot be weakened by the alias.

### Templates and instances

An uninstantiated template such as `example@.service` is discovered as template metadata but is not returned as a manageable `SystemService`: it has no concrete runtime identity and must not be assigned invented active or sub-state values. Template metadata is retained only inside the adapter snapshot so a concrete instance can be classified and resolved.

A concrete instance such as `example@tenant.service` is included when it is explicitly installed or currently loaded. It is validated and authorized as its full escaped systemd unit name. Serval does not enumerate hypothetical instance names and does not accept a template name where a concrete service operation is required.

## D-Bus compatibility assumptions

The minimum supported systemd API level is version 249. This baseline covers Ubuntu 22.04 LTS. Later supported platform baselines currently include systemd 255 on Ubuntu 24.04 LTS, 257 on Debian 13, and 259 on Ubuntu 26.04 LTS. Distribution servicing revisions may change without changing this decision.

There is no textual command output to parse. The adapter relies only on the stable, typed D-Bus contracts documented by systemd:

- manager methods `ListUnitFiles`, `ListUnitsByPatterns`, and `ListUnitsByNames`;
- unit properties `Id`, `Names`, `Description`, `LoadState`, `ActiveState`, and `SubState`;
- the manager `Version` property.

D-Bus array/structure signatures, object paths, UTF-8 strings, and standard D-Bus error names are decoded by the D-Bus client library. The adapter does not depend on field order in JSON, column spacing, terminal width, locale, color, pager behavior, or `systemctl` exit text. Additive properties and unknown state strings are tolerated; a missing required member, incompatible signature, invalid UTF-8, or malformed unit record is an incompatible-platform failure.

The real-systemd CI matrix for the implementation must exercise the lowest supported API level and the systemd versions shipped by every supported Ubuntu LTS and Debian stable release on both supported architecture families where runners are available. Adding a distribution or raising its systemd major version requires the discovery contract tests to pass before that platform is declared supported.

Version references:

- [systemd D-Bus API](https://www.freedesktop.org/software/systemd/man/latest/org.freedesktop.systemd1.html)
- [systemd unit and template listing semantics](https://www.freedesktop.org/software/systemd/man/latest/systemctl.html)
- [Ubuntu systemd packages](https://packages.ubuntu.com/search?keywords=systemd&searchon=names&suite=all&section=all)
- [Debian 13 systemd package](https://packages.debian.org/trixie/systemd)

## Trust boundary and authorization

Discovery adds one minimal privileged capability: read service metadata from PID 1 over the local system bus. It does not grant generic D-Bus access, process execution, filesystem reads, or service lifecycle operations.

For the future IPC operation:

- `List` has no caller-controlled service, path, command, or D-Bus member; `Inspect` accepts only one bounded `SystemServiceId`. Caller cancellation and correlation metadata are also untrusted inputs.
- Agent authenticates the IPC peer and derives the principal from verifiable connection/delegation context. It never trusts a caller-supplied actor, role, group list, or `IsAuthorized` flag.
- Agent independently validates the requested ID, resolves it through systemd, then enforces `Service.View` and protected-service policy against the canonical ID before returning metadata.
- Policy lookup failure, unverifiable identity, malformed input, a protected target, or a Serval-owned privileged target fails closed.
- Responses contain only service identity, description, and state metadata. Environment values are neither requested nor returned, logged, audited, cached in SQLite, or placed in exception messages.
- Audit metadata may contain actor, operation, canonical service ID, outcome, and correlation ID. It must not contain D-Bus payload dumps or environment values.

Using a typed D-Bus client removes shell injection and command-string injection paths. Rejecting caller-supplied paths and arbitrary D-Bus members removes traversal and generic confused-deputy paths. Canonicalizing before policy checks prevents alias-based arbitrary targeting. The implementation must still defend against malformed D-Bus data, unit replacement races, spoofed IPC identity, authorization bypass, protected-service aliases, and resource exhaustion from oversized replies.

## Failure, timeout, and cancellation

The adapter uses an internal, non-caller-controlled deadline of 30 seconds for a complete list snapshot and 5 seconds for a single inspection. Each D-Bus call is linked to the earlier of that deadline and the caller's cancellation token.

- Caller cancellation stops outstanding D-Bus work and completes as cancellation; it is not translated to `NotFound` or a successful partial result.
- Deadline expiry produces a distinct typed timeout failure so the Agent can return a stable unavailable/timeout response. It is not retried indefinitely.
- D-Bus disconnection, access denial, incompatible signatures, malformed replies, and unexpected manager errors fail the entire snapshot. The previous successful in-memory snapshot, if caching is later introduced, is not overwritten by partial data and must not be presented as fresh without an explicit stale marker.
- A unit disappearing between enumeration and resolution is retried once within the same deadline. If it is still absent, list omits that candidate; inspection returns `NotFound`. No other automatic retry is performed.
- Failures return bounded, sanitized diagnostics. Raw D-Bus payloads and environment data are never included in logs, exceptions, audit records, or telemetry.

Cancellation and discovery failure never trigger a service start, stop, restart, reload, enablement change, unit-file write, or `daemon-reload`.

## Consequences

The implementation will need a D-Bus client that supports cancellation, call deadlines, the system bus, and the required typed signatures on .NET 10. Selecting that package is deferred until the adapter implementation issue so dependency review can happen with executable tests.

Inventory tests must cover inactive services, aliases resolving to one canonical item, an uninstantiated template, concrete instances, transient/generated loaded services, unknown state values, malformed names and replies, timeouts, cancellation, authorization denial, protected services, and Serval's own units. Critical semantics must also be verified against disposable units on real systemd; mocks alone are insufficient.
