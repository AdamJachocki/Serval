# Typed systemd D-Bus transport

Status: Accepted  
Date: 2026-09-03  
Issue: [#10](https://github.com/AdamJachocki/Serval/issues/10)

## Dependency decision

Serval uses Tmds.DBus.Protocol pinned to version 0.95.0. It is maintained, MIT
licensed, targets .NET 6 or later (and therefore .NET 10), and supports trimming
and NativeAOT. Serval keeps a small typed proxy in source so the build does not
make a source generator consume unrelated Roslyn `AdditionalFiles` supplied by
IDE analyzers such as SonarLint. The proxy implements only the fixed calls and
property reads listed below.

The older reflection-based Tmds.DBus package is in maintenance mode and is not
used. Tmds.Systemd is also not used because its last release predates the
selected protocol client and it would expose a broader API than the discovery
contract requires.

The source XML checked into Serval.Systemd documents the intentionally minimal subset of
systemd's stable D-Bus contract. Updating the package, proxy or XML requires
the unit tests and real-systemd contract test to pass.

## Fixed endpoint and narrow contract

The production transport always uses:

- DBusAddress.System;
- destination org.freedesktop.systemd1;
- manager object /org/freedesktop/systemd1;
- manager interface org.freedesktop.systemd1.Manager;
- unit interface org.freedesktop.systemd1.Unit.

It exposes only:

- the manager Version property;
- ListUnitFiles;
- ListUnitsByPatterns with fixed empty states and the fixed *.service pattern;
- ListUnitsByNames with validated SystemServiceId values;
- six fixed property reads for Id, Names, Description, LoadState, ActiveState
  and SubState for an opaque unit reference previously returned by the same
  transport instance. The transport never uses Properties.GetAll.

Callers cannot provide a bus address, destination, manager path, interface,
method, property or discovery pattern. A unit object path is represented by an
opaque reference bound to the transport instance that issued it. No process,
shell, systemctl invocation or command construction exists in this boundary.

One transport instance represents one bounded discovery or inspection
operation. Its deadline is positive, cannot exceed two minutes, and is linked
with caller cancellation for every connection and D-Bus call. Cancellation or
deadline expiry disposes the connection so the outstanding protocol operation
does not continue in the background. The intended adapter deadlines remain 30
seconds for a complete snapshot and 5 seconds for one inspection, as selected
by the discovery strategy.

## Reply and failure boundary

D-Bus replies are untrusted. The transport bounds collection sizes, paths,
descriptions, states and version strings; validates every returned service name;
accepts only systemd unit object paths; requires all selected unit properties;
and rejects malformed or incompatible replies.

Failures are mapped to these stable categories:

- unavailable;
- remote D-Bus error;
- incompatible signature or missing generated property;
- malformed reply;
- deadline exceeded.

Caller cancellation remains cancellation. Exception messages are fixed and do
not include raw replies or remote error messages. A syntactically safe,
255-character-bounded D-Bus error name may be returned as metadata; all other
remote error names are discarded.

## Privileged capability analysis

The future runtime owner is serval-agent. This transport is internal to
Serval.Systemd; it is not itself an IPC operation or an authorization boundary.

Attacker-controlled inputs are limited to:

- caller cancellation;
- for name-based resolution, validated SystemServiceId instances;
- D-Bus replies from PID 1, including object paths and metadata.

The exact privileged capability is read-only access to service metadata exposed
by the local system manager. It is needed because the accepted discovery
strategy requires installed and loaded unit information that must be resolved
to canonical identities. It grants no process execution, filesystem access,
lifecycle action, configuration mutation or generic D-Bus access.

The smallest future Agent operations remain List with no service target and
Inspect(SystemServiceId). Principal identity must come from authenticated,
verifiable Agent IPC context. The Agent must independently resolve canonical
identity and enforce Service.View, protected-service policy and exclusion of
Serval's privileged units before returning data. None of those decisions are
delegated to this transport or to Web.

Responses contain only service identity, description and state metadata.
Environment values are not requested. Future audit records may contain the
verified actor, operation, canonical service ID, outcome and correlation ID,
but never raw D-Bus payloads.

Controls for the relevant threats are:

- command injection: no command or child-process capability exists;
- arbitrary D-Bus/confused deputy: all endpoint and member identifiers are
  fixed and unit paths are opaque, instance-bound references;
- arbitrary service targeting: input names are SystemServiceId values and
  future Agent policy checks canonical names after resolution;
- malformed replies/resource exhaustion: values and collections are bounded
  and mapped to sanitized typed failures;
- alias bypass: future authorization and protection checks operate on the
  canonical ID and all resolved names;
- path traversal, symlink, TOCTOU and arbitrary writes: this read-only transport
  performs no filesystem access.

Malformed input, policy lookup failure, unverifiable identity, unauthorized or
protected services remain fail-closed responsibilities of the future Agent
operation. Adding such an operation requires its own permitted, denied,
malformed, malicious, unauthorized and protected-service tests.

## Verification

Unit tests use a protocol seam below the narrow transport to cover fixed typed
calls, valid and malformed replies, remote errors, incompatible signatures,
deadline expiry, cancellation and opaque-reference ownership. That seam remains
internal and is not a privileged application capability.

A gated integration test calls every selected manager method and the selected
unit properties against a real system manager. CI covers systemd 249, 255 and
259 on Ubuntu 22.04, 24.04 and 26.04 respectively, plus systemd 257 in a
privileged Debian 13 systemd container. Each architecture executes natively:
Ubuntu directly on its matching hosted runner and Debian in a container on the
matching host, without CPU emulation. The later inventory integration issue owns
disposable-unit discovery semantics beyond this transport contract.
