# Application-facing systemd service inventory

Issue: #16

`SystemdServiceInventory` composes the internal enumerator and inspector behind
`ISystemServiceInventory`. Results contain canonical identities, current state
metadata and built-in protected-service classification. Serval privileged units
remain absent. List results are sorted by canonical ID using ordinal comparison.

Enumeration publishes no partial snapshot. A unit that disappears during its
single bounded re-resolution is omitted; every other enumeration or inspection
failure remains a typed failure and is propagated to the caller. Caller
cancellation is linked through connection, enumeration, inspection, retry and
final result publication, and is never converted to `NotFound` or a partial list.

## Boundary analysis

Listing accepts only caller cancellation. Inspection additionally accepts one
bounded `SystemServiceId`; systemd replies and correlation metadata remain
untrusted. The exact capability is read-only access to the fixed system-manager
metadata calls already defined by the typed D-Bus adapter. No command, path,
arbitrary D-Bus member, asserted principal, authorization flag or secret value is
accepted or returned.

This composition adds no IPC endpoint or new root capability. The future Agent
must authenticate its peer, derive the principal from trustworthy connection
context, revalidate inspected identifiers, enforce `Service.View`, and apply
protected-service policy against the canonical result before exposure. Built-in
classification is metadata, not authorization. Policy lookup failure must fail
closed. Serval privileged identities are excluded before this adapter publishes a
result and cannot be enabled through normal permissions.

Typed fixed D-Bus calls and bounded identifiers prevent shell injection,
arbitrary process launch, traversal and arbitrary service targeting. The
operation performs no filesystem writes, so symlink, TOCTOU and arbitrary-write
paths are absent. It logs, audits and persists no environment values or payloads.
Future audit records may contain only verified actor, operation, canonical service
ID, outcome and bounded correlation metadata.

Tests use injected application-adapter primitives rather than process or command
details. Existing unit and real-systemd coverage verifies canonical aliases,
templates and instances, disappearing units, malformed replies, protected
classification, privileged-unit exclusion, timeouts and cancellation.
