# Serval privileged-unit exclusion

Normal systemd discovery and inspection never return `serval-agent.service`,
`serval-agent@.service`, or any concrete `serval-agent@` instance. The rule is a
fixed, ordinal, case-sensitive server-side policy in `Serval.Systemd`; it has no
application setting, request flag, ACL override, or Web-side bypass.

Enumeration applies the rule only after validated identities have been resolved
and every canonical record's aliases have been merged. A privileged alias therefore
excludes the entire canonical record even if an ordinary alias or canonical name
was observed first. Template aliases are never published because the unit-file
listing does not expose their authoritative target; excluded canonical templates
are also removed from adapter metadata.
Inspection rejects a direct excluded name before connecting to D-Bus and converts
an ordinary alias that resolves to an excluded identity into `NotFound`.

The exclusion is intentionally narrower than built-in protected-service
classification. Other protected services such as `ssh.service` and
`systemd-journald.service` remain in the internal snapshot with `IsProtected` set;
future Agent policy decides whether a verified principal may view them.

## Privileged-boundary analysis

Attacker-controlled inputs remain the inspected service identifier, cancellation
and correlation metadata, and all systemd D-Bus replies. Enumeration accepts no
caller-selected unit, path, command, D-Bus member, identity, role or authorization
assertion. This change adds no root capability or IPC contract: the existing
read-only capability is still limited to fixed system-manager metadata calls.

Names are validated as bounded `SystemServiceId` values before reuse. The policy
examines the authoritative canonical ID and all validated aliases, fails closed by
omitting the complete identity, and performs no filesystem access or process
execution. Consequently it introduces no shell injection, traversal, symlink,
TOCTOU, arbitrary-write or arbitrary-service-operation path. It returns no
environment values, credentials or new audit payloads. Future Agent exposure must
still authenticate the IPC peer, derive a trustworthy principal, enforce
`Service.View` and protected-service policy, and use bounded metadata-only audit
records. Policy lookup is unnecessary because this exclusion is compiled in and
cannot be disabled by ordinary application input.

## Verification

Unit tests cover the exact service, template and instances, canonical and alias
matches, alias ordering, later alias merging, template aliases, case-sensitive lookalikes, unrelated
Serval names, and other protected services. The disposable real-systemd harness
uses UUID-scoped concrete names, verifies their absence manager-wide before
creation, and covers a `serval-agent@<uuid>.service` identity, its ordinary alias,
a negative lookalike and fail-closed omission of unresolved template aliases.
It never creates or shadows the fixed `serval-agent@.service` template. Fixture
sources remain unchanged; no production unit is modified or restarted.
