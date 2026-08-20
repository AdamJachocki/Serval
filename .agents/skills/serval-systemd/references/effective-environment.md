# Effective environment and Serval-owned overrides

Read this reference when work interprets environment sources, precedence, drop-ins, or filesystem mutations.

## Source model

- Inventory the unit and its drop-ins without treating `systemctl cat` output as a resolved runtime environment.
- Read all applicable `Environment=` assignments and `EnvironmentFile=` declarations, including multiple files and optional-file declarations. Do not execute their contents or source them through a shell.
- Preserve source provenance separately from the effective key/value view so the UI can explain that a value is inherited or overridden without exposing the value itself.
- Apply systemd ordering and precedence, not an invented merge rule. Later environment files override earlier files, and values read from `EnvironmentFile=` override values supplied by `Environment=`. Empty/reset directives and drop-in ordering must be covered by the parser/design when applicable.
- Treat environment-file values as secrets from the moment they are read. Do not persist discovered values in SQLite or include them in logs, telemetry, audit data, exceptions, fixtures, or snapshots.

## Serval override model

- A Serval-owned, lexically ordered drop-in references a Serval-owned environment file whose assignments take precedence over the inherited sources. The exact naming scheme must be deterministic and collision-resistant within `/etc/serval` and the appropriate systemd drop-in directory.
- Do not copy inherited values into the Serval file. Store only active Serval overrides so removal reveals the current underlying configuration rather than a stale copy.
- Removing one override rewrites or removes only Serval-owned state. Remove an empty Serval environment file/drop-in when it is no longer needed.
- Write a complete replacement to a safely created temporary file in the same filesystem, set restrictive metadata, flush as appropriate, and atomically rename it into place. Validate the destination and defend against symlinks before and during replacement.
- A failed write must leave the previous complete configuration usable. Run `daemon-reload` only after filesystem mutation succeeds, report reload failure without restarting, and retain enough non-secret state for a safe retry or diagnosis.
- Save/apply may update files and perform the required reload, but it must not call start, stop, try-restart, reload-or-restart, or restart.

Confirm any systemd semantic relied upon by the implementation against the systemd versions shipped by Serval's supported Debian and Ubuntu releases, then lock it down with a real-systemd integration test.
