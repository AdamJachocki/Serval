# Systemd service inspection

Issue: #12

`SystemdServiceInspector` performs internal read-only inspection for the future
Agent over the existing typed system-bus transport. It accepts one bounded
`SystemServiceId` and cancellation token. Null, invalid and uninstantiated
template identifiers are rejected before connection. Concrete instances are
resolved without starting them.

The operation checks the supported manager version, calls `ListUnitsByNames`
with exactly the requested name and reads the returned unit's six metadata
properties. A single result must identify the requested name and the listed name
in `Names`, alongside its canonical `Id`. Names are validated, deduplicated and
sorted ordinally. Unrelated or ambiguous replies fail closed. Aliases return the
canonical service and retain all known names. Description and state come from
the property read; unknown bounded state strings are preserved.

An empty lookup, `NoSuchUnit`, unit-property `UnknownObject` or `not-found` load
state triggers at most one fresh lookup within the same deadline. Continued
absence returns the explicit internal `NotFound` result with the requested ID.
Manager-object `UnknownObject`, access denial and other transport failures remain
bounded typed failures. They are not retried or converted to absence. The fixed
5-second deadline includes connection, version check, lookup, properties and
retry. Caller cancellation remains cancellation, including when both tokens are
cancelled. The transport is disposed on success, absence, failure and cancellation.

## Capability and trust boundary

Untrusted inputs are the requested identifier, cancellation and D-Bus replies.
The capability is limited to resolving one system service and reading identity,
description and state from PID 1. Callers supply no bus destination, object path,
principal, authorization assertion, command or environment value. The existing
transport issues opaque unit references and reads only its fixed metadata
allowlist. Production inspection performs no process execution, filesystem
access, lifecycle operation, configuration change or environment read.

This change adds no public root/IPC endpoint and registers nothing with Web.
`Found` is internal, unauthorised metadata, not an application response. Future
Agent exposure must authenticate the peer, derive a verifiable principal,
revalidate the request and check `Service.View` and protected-service policy
against canonical ID and every retained name. Serval's privileged units must
always remain excluded. Policy failure must fail closed. Those endpoint-level
denied/protected tests belong to that future boundary. No audit, telemetry or
persistence is added; eventual audit must use verified actor, canonical ID,
operation and outcome only. Payloads and environment values must never enter
diagnostics or storage.

Name bounds and fixed typed calls prevent arbitrary command/path targeting;
canonical identity consistency prevents alias substitution. Disappearance is
bounded by one retry, with no partial result. Inspection does not assert that
the service remains unchanged after the read; future mutations must resolve and
authorize their own current target.

## Verification

Unit tests exercise canonical/alias lookup, current and unknown states, concrete
instances, input and reply validation, ambiguity, absence and reappearance,
version compatibility, access denial, disconnection, deadline and cancellation
during connection, lookup, property read and retry. They run through the real
typed transport with a protocol fake, which rejects broad enumeration calls.

The existing real-systemd CI matrix runs the inspection tests alongside transport
and enumeration tests on configured Ubuntu/Debian versions and architectures.
The disposable fixture harness now also creates a failed unit and a masked unit.
Tests verify aliases, inactive/active/failed/masked states, instances, templates,
nonexistent targets and unchanged fixture bytes/symlinks. Fixture creation and
cleanup are test-only system operations, never capabilities of the adapter.
