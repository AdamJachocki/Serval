# Systemd service identity

Issue: #13. This refines the [accepted discovery strategy](systemd-discovery-strategy.md)
and applies equally to enumeration and inspection in `Serval.Systemd`.

## Canonical names and aliases

`SystemdServiceIdentity` validates the unit object's `Id` and `Names` together.
`Id` is authoritative, even when an alias sorts before it. `Names` must contain
that ID and every requested or listed name used to resolve the object. Names
are validated, deduplicated and sorted using ordinal, case-sensitive comparison.
The canonical name remains in `Names` for future Agent policy evaluation.

An object is read once during enumeration, but every subsequent listing using
that object is still checked against its names. Multiple objects with the same
canonical ID merge their name sets; the first observation in ordinal listing
order supplies state. Distinct IDs are never merged by alias, object path,
template prefix, description, or decoded instance string.

Each name, including each canonical name, has exactly one canonical owner within
the snapshot. Conflicting ownership (including two-way or longer alias cycles)
fails the entire snapshot with a sanitized `MalformedReply`. No partial result
is published and no alias chain is recursively traversed. `FollowedUnit` is
validated by the transport but is not an alias-target field: it describes state
following and cannot override `Id`/`Names`. Filesystem symlinks are resolved by
systemd, not followed by the adapter. Missing units keep the existing bounded
retry behavior; other manager errors remain typed failures.

## Templates and concrete instances

Names without `@` are plain services. The first `@` separates the template prefix
from the instance; an empty instance identifies an uninstantiated template.
Templates, including template aliases, remain sorted internal metadata only.
They are not queried for invented runtime state or returned as manageable services.
Inspection rejects a template before connecting to D-Bus.

A concrete instance is returned only when installed or loaded. Its complete,
escaped, case-sensitive name is its identity: `worker@A.service`,
`worker@a.service`, `worker@tenant-1.service` and `worker@tenant\x2d1.service`
remain distinct. No hypothetical instances are created from template metadata.

Plain services can have only plain aliases. Instance aliases must have the exact
same instance part as their canonical target; the template prefix may differ.
For example, `helper@tenant.service` can identify `worker@tenant.service`, but
cannot identify `worker@other.service`. Template names cannot appear in a
concrete service's `Names`. These restrictions follow
[systemd.unit](https://github.com/systemd/systemd/blob/main/man/systemd.unit.xml).

## Boundary and verification

This remains an internal, read-only adapter intended for Agent. Untrusted inputs
are requested service IDs and D-Bus identity/state metadata. No new privileged
capability, IPC endpoint, filesystem access or process execution is introduced.
Future Agent integration must authenticate the principal independently, apply
`Service.View` and protected-service policy to canonical ID and all names, and
audit only non-secret metadata. These results are not authorization decisions.

Unit tests cover deterministic aliases, shared objects, conflicting ownership,
cycles, incompatible instance aliases, template exclusion and multiple escaped
and case-distinct instances. Real-systemd tests exercise plain and instance
aliases, template aliases and installed/loaded instances through the existing
CI fixture and platform matrix. The harness creates only disposable UUID-prefixed
units under `/run/systemd/system`, cleans them up, and checks fixture contents
and alias targets remain unchanged by discovery/inspection.
