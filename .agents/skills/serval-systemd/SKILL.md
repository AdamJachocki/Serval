---
name: serval-systemd
description: Apply Serval's mandatory systemd rules whenever work touches system services, unit files, Environment=, EnvironmentFile=, drop-ins, effective service configuration, service discovery or lifecycle, daemon-reload, or Serval's systemd integration.
---

# Serval systemd

Use this skill for implementation, design, review, and tests involving systemd. For mutations or any Web-to-Agent/systemd privileged path, also load `serval-privileged-operation`.

## Non-negotiable design

- Support system-level services only. Do not add user-service behavior.
- Never edit vendor-owned or user-owned unit files or environment files. Read their `Environment=` and `EnvironmentFile=` inputs, but treat them as immutable.
- Model and present the effective configuration. A Serval edit changes that effective configuration; it does not rewrite the source that supplied the underlying value.
- Persist Serval values only in a Serval-owned environment file referenced by a Serval-owned systemd drop-in. Removing a Serval override must remove only the Serval-owned value/artifact and naturally expose the underlying systemd value again.
- Keep save/apply and restart as separate, explicit operations. Saving must never implicitly start, stop, or restart a service.
- Never expose Serval's own privileged components to ordinary discovery or management flows. Protected-service policy takes precedence over ACLs.
- Keep systemd behind an application-facing abstraction; do not leak process invocation details into the application layer.

## Required workflow

1. Classify the change as discovery/read, Serval-owned configuration mutation, daemon reload, or service lifecycle operation. Identify which process performs it.
2. For environment discovery, precedence, drop-in layout, removal, or writes, read [references/effective-environment.md](references/effective-environment.md) before designing or changing behavior.
3. Validate every unit identifier and every path at the privileged boundary. Reject malformed, malicious, non-system, protected, and Serval-owned privileged targets before filesystem access or process execution.
4. Use a typed API when available. Otherwise invoke a fixed executable directly with separately supplied, validated arguments. Never use `/bin/sh -c`, a shell script assembled from request data, or command-string concatenation.
5. Restrict writes to the exact Serval-owned locations. Make file replacement atomic, set ownership and permissions deliberately, and avoid symlink, traversal, and TOCTOU paths. Never follow an input-controlled path to decide a privileged write target.
6. Treat `daemon-reload` as an explicit systemd operation after a successful configuration change when needed. It does not authorize or imply restart.
7. Preserve environment values as secrets in results, errors, logs, audit records, test output, and snapshots.
8. Before completion, read [references/integration-tests.md](references/integration-tests.md) and implement the applicable permitted and denied cases. Critical behavior must run against real Linux/systemd in CI; mocks alone are insufficient.

Reject a design that cannot restore base configuration by removing only Serval-owned state, combines save with restart, or requires modifying an existing non-Serval source.
