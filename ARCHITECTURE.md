# Serval Architecture

This document describes the high-level architecture of Serval.

It defines the main runtime boundaries, module responsibilities, and dependency direction. Implementation details and code locations are documented separately.

For the current repository structure and implementation entry points, see [`docs/code-map.md`](docs/code-map.md).

## System overview

Serval is a security-first Linux administration application implemented as a modular monolith.

The system separates network-facing application behavior from privileged operating-system operations.

```text
Browser
   |
   v
Serval.Web
(unprivileged)
   |
   v
local-only IPC
   |
   v
Serval.Agent
(privileged)
   |
   +-- systemd
   +-- protected filesystem operations
```

`Serval.Web` must never require root privileges.

Operations requiring elevated Linux privileges belong behind the `Serval.Agent` boundary.

Detailed trust and security requirements are defined in [`SECURITY.md`](SECURITY.md).

## Architectural style

Serval is a modular monolith with explicit module boundaries.

The architecture favors:

* clear dependency direction,
* application-facing abstractions around external systems,
* minimal privileged functionality,
* separation of domain and infrastructure concerns,
* testable application logic.

New distributed services should not be introduced without a concrete requirement that cannot reasonably be handled within the modular monolith.

## Modules

| Module                  | Responsibility                                                |
| ----------------------- | ------------------------------------------------------------- |
| `Serval.Domain`         | Domain concepts and domain rules                              |
| `Serval.Application`    | Application use cases and technology-independent abstractions |
| `Serval.Infrastructure` | General infrastructure implementations                        |
| `Serval.Systemd`        | systemd-specific integration                                  |
| `Serval.Web`            | Network-facing web application and UI                         |
| `Serval.Agent`          | Minimal privileged local process                              |

## Dependency direction

The intended dependency direction is:

```text
Serval.Domain
      ^
      |
Serval.Application
      ^
      |
 ┌────┼──────────────┬─────────────┐
 |    |              |             |
Web  Agent       Infrastructure   Systemd
```

Equivalent project dependency direction:

```text
Domain <- Application <- Web
                     <- Agent
                     <- Infrastructure
                     <- Systemd
```

Domain and application layers must not depend on infrastructure-specific implementations.

Technology-specific integrations should remain behind application-facing abstractions.

## Runtime boundaries

### Serval.Web

`Serval.Web` is the network-facing application process.

It hosts the UI and executes unprivileged application behavior.

Privileged operating-system operations must not be implemented directly in this process.

### Serval.Agent

`Serval.Agent` is the minimal privileged local process.

It performs narrowly defined operations that require elevated Linux privileges.

The Agent is a trust boundary and must expose specific capabilities rather than acting as a generic shell, command runner, or unrestricted filesystem API.

Communication between Web and Agent is local-only.

## External boundaries

Serval integrates with external operating-system facilities through explicit adapters and boundaries.

* **systemd** — integration belongs primarily to `Serval.Systemd`; privileged execution is performed through `Serval.Agent` where required.
* **Linux PAM** — provides Linux-backed authentication.
* **Filesystem** — privileged filesystem changes are performed only through explicitly defined privileged operations.
* **SQLite** — stores non-secret Serval application state through infrastructure abstractions.


## Code map

The physical structure of the repository, important namespaces, implementation locations, entry points, and corresponding tests are documented in:

[`docs/code-map.md`](docs/code-map.md)

The code map describes the current implementation and may evolve more frequently than this document.

## Architecture decisions

Detailed architectural and technical decisions should not be accumulated in this file.

Long-lived decisions belong under:

[`docs/`](docs/)

Changes to externally observable or contractually significant system behavior belong in OpenSpec.

## Related documentation

* [`AGENTS.md`](AGENTS.md) — entry point for AI agents.
* [`SECURITY.md`](SECURITY.md) — security model and mandatory constraints.
* [`CONTRIBUTING.md`](CONTRIBUTING.md) — development workflow and quality requirements.
* [`docs/code-map.md`](docs/code-map.md) — current implementation map.
* [`docs/`](docs/) — Long-lived technical and architectural decisions..
* [`openspec/specs/`](openspec/specs/) — accepted system behavior.
* [`openspec/changes/`](openspec/changes/) — proposed and in-progress changes.