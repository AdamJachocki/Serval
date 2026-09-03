# Architecture

This document records the bootstrap architecture. It is a constraint for future implementation, not evidence that the planned runtime integrations already exist.

## Runtime boundary

Serval is designed as at least two local processes:

```text
Browser
   |
   v
Serval.Web (network-facing, never root)
   |
   v
local-only authenticated IPC (not implemented)
   |
   v
Serval.Agent (minimal privileged component; currently inert)
   |
   v
systemd and Serval-owned protected files (not implemented)
```

Future Agent-side code must assume that Web is compromised. The Agent must independently authenticate the peer and validate, authorize, and audit every narrow operation. A generic command runner or generic privileged file API is forbidden.

## Modules

| Project | Responsibility at bootstrap | Allowed production direction |
| --- | --- | --- |
| `Serval.Domain` | Empty domain boundary | Depends on no Serval project |
| `Serval.Application` | Empty use-case boundary | May depend on Domain |
| `Serval.Infrastructure` | Empty implementation boundary | May depend on Application |
| `Serval.Systemd` | Empty future systemd adapter boundary | May depend on Application |
| `Serval.Web` | Framework-generated Razor Pages skeleton | May depend on Application |
| `Serval.Agent` | Inert executable and future privileged host boundary | May depend on Application |

The project references enforce this graph:

```text
Serval.Domain <- Serval.Application <- Serval.Infrastructure
                       ^
                       |-- Serval.Web
                       |-- Serval.Agent
                       `-- Serval.Systemd
```

Application-facing abstractions belong in `Serval.Application`; concrete systemd details must not leak into it. References must be added only when current implementation needs them.

The accepted [systemd service discovery strategy](systemd-discovery-strategy.md) uses the system manager's typed D-Bus API. Its future runtime owner is `Serval.Agent`; it must not be wired directly into `Serval.Web`.

## Data and secret boundaries

SQLite is reserved for non-secret application state such as ACLs, audit metadata, and configuration metadata. Environment-variable values and Linux passwords must never be stored in SQLite or emitted to logs, audit records, telemetry, exceptions, test output, or snapshots.

The future systemd adapter may read existing environment sources but must treat them as immutable. Serval changes must use only Serval-owned drop-ins and environment files. Removing a Serval override must naturally reveal the underlying configuration. Saving and restarting are separate explicit operations.

## Verification boundaries

Unit tests live next to their corresponding module under `tests/`. Security-sensitive operations require permitted, denied, malformed, malicious, unauthorized, and protected-target cases as applicable. Critical systemd semantics require disposable fixtures on a real Linux/systemd environment in CI in addition to unit tests.

The bootstrap has no systemd behavior, so it does not yet add a privileged or real-systemd test job.
