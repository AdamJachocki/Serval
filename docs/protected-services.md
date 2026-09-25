## Initial curated set

These are deliberate conservative defaults for critical infrastructure, not an
exhaustive inventory of every distribution's critical services.

| Exact name | Reason |
| --- | --- |
| serval-agent.service | Serval's privileged component must never be managed through normal permissions. |
| dbus.service | System message bus used by privileged services and Serval discovery. |
| dbus-broker.service | Alternative system message bus implementation. |
| systemd-journald.service | System journal and operational diagnostics. |
| systemd-logind.service | Login sessions and seat lifecycle. |
| systemd-udevd.service | Device event processing and device availability. |
| systemd-networkd.service | Host network configuration and connectivity. |
| systemd-resolved.service | Host name resolution. |
| NetworkManager.service | Host network configuration and connectivity. |
| networking.service | Traditional host network configuration. |
| ssh.service | SSH administration access, including recovery access. |
| sshd.service | Alternative SSH daemon unit name. |
| polkit.service | Privileged operation authorization infrastructure. |
| systemd-user-sessions.service | System-wide admission of user sessions. |

| Template family | Reason |
| --- | --- |
| serval-agent@.service | Instantiated privileged Serval components. |
| systemd-journald@.service | Journal namespace instances. |
| ssh@.service | Per-connection SSH daemon instances. |
| sshd@.service | Alternative per-connection SSH daemon instances. |
| user@.service | System units hosting user managers and their sessions. |

A family matches precisely its name followed by `@`, any validated instance
(including the empty template instance), and the `.service` suffix. For example,
`ssh@connection.service` matches; `ssh-backup.service`, `worker@ssh.service`, and
`SSH.service` do not. Escaped instance strings remain opaque. There is no broad
`systemd-*`, substring, case folding, or escape decoding rule. Templates remain
separate adapter metadata and are never returned as manageable concrete services.
The `user@` rule concerns system-manager units, not discovery of user services.

## Trust boundary

This is pure in-memory discovery/read metadata within Serval.Systemd, whose future
runtime owner is Agent. Inputs are validated canonical IDs and Names from the
existing D-Bus boundary; raw names and malformed replies are still rejected there.
No new privileged capability, IPC request, filesystem access, command execution,
identity assertion, logging, persistence, or environment data is introduced.

The metadata is not an authorized response. Future Agent operations must derive a
verifiable principal, check the operation's resource permission, and apply protected
policy before exposure or mutation. An unprotected result does not grant access.
Serval's privileged components must remain excluded even if later local policy
allows other protected services. This task introduces no override, Web bypass,
configuration mechanism, or service lifecycle operation.

## Verification

Unit tests cover every entry and family, unrelated lookalikes, aliases and merged
names. Existing malformed-input tests continue to validate the adapter boundary.
Real-systemd tests check protected journal metadata and ordinary disposable
fixtures through both discovery paths; these run in the existing Linux CI matrix.
No real protected service is modified or restarted.
