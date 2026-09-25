# Serval Code Map

This document maps Serval's current implementation to repository locations.

Use it to quickly identify where to start reading or where a change is likely to belong.

For architectural boundaries and dependency rules, see [`../ARCHITECTURE.md`](../ARCHITECTURE.md).

This document describes the **current implementation**, not planned functionality.

## Repository map

```text
Serval/
├── src/                     Production projects
├── tests/                   Automated tests
├── docs/                    Technical and design documentation
├── openspec/                Specifications and change artifacts
├── .agents/                 Agent skills and workflows
├── .github/                 CI and GitHub configuration
├── Serval.slnx              Solution definition
├── Directory.Build.props    Shared build configuration
├── Directory.Packages.props Central package versions
└── global.json              .NET SDK selection
```

## Production code

### `src/Serval.Domain`

Core domain types.

Current implementation is concentrated under:

```text
src/Serval.Domain/
└── Services/
    ├── SystemService.cs
    ├── SystemServiceId.cs
    ├── SystemdActiveState.cs
    ├── SystemdLoadState.cs
    └── SystemdSubState.cs
```

Start here when changing the representation or invariants of system services.

Corresponding tests:

```text
tests/Serval.Domain.Tests/Services/
```

---

### `src/Serval.Application`

Application-facing contracts and technology-independent result models.

Current service-related code:

```text
src/Serval.Application/Services/
├── ISystemServiceInventory.cs
├── ISystemServiceEnvironmentReader.cs
├── ServiceInspectionResult.cs
├── ServiceEnvironmentReadResult.cs
├── EnvironmentMetadata.cs
└── EnvironmentValues.cs
```

Important entry points:

* `ISystemServiceInventory` — application contract for listing and inspecting services.
* `ISystemServiceEnvironmentReader` — application contract for reading service environment configuration.
* `ServiceInspectionResult` — service inspection result exposed to application consumers.
* `ServiceEnvironmentReadResult` — environment inspection result exposed to application consumers.
* `EnvironmentValues` / `EnvironmentMetadata` — application-level environment representations.

Start here when defining what the application needs from system integrations without introducing systemd-specific implementation details.

Corresponding tests:

```text
tests/Serval.Application.Tests/Services/
```

---

### `src/Serval.Systemd`

Current systemd adapter and the most developed infrastructure area.

#### Service inventory

```text
SystemdServiceInventory.cs
SystemdServiceEnumerator.cs
SystemdServiceInspector.cs
SystemdServiceIdentity.cs
ServiceEnumerationSnapshot.cs
SystemdServiceInspectionResult.cs
```

Key flow:

```text
ISystemServiceInventory
        |
        v
SystemdServiceInventory
     /          \
    v            v
Enumerator    Inspector
    \            /
     v          v
       systemd
```

Start with:

* `SystemdServiceInventory.cs` for the application-facing adapter,
* `SystemdServiceEnumerator.cs` for service enumeration,
* `SystemdServiceInspector.cs` for inspection of a specific service.

#### D-Bus

```text
DBus/
├── ISystemdDbusProtocol.cs
├── ISystemdDbusTransport.cs
├── SystemdDbusTransport.cs
├── TmdsSystemdDbusProtocol.cs
├── SystemdDbusProxies.cs
└── SystemdDbusException.cs
```

This area contains the systemd D-Bus transport and protocol boundary.

D-Bus interface source:

```text
DbusXml/org.freedesktop.systemd1.xml
```

Start here when changing how Serval communicates with the systemd manager.

#### Environment handling

```text
EnvironmentFileParser.cs
EnvironmentFileParseResult.cs
LoadedEnvironmentDecoder.cs
EnvironmentReadLimits.cs
SystemdEnvironmentOperationContext.cs
SystemdEnvironmentSourceReader.cs
SystemdEnvironmentSourceReadResult.cs
SystemdEnvironmentComposer.cs
SystemdServiceEnvironmentReader.cs
SystemdSourcePath.cs
LinuxSystemdSourceFileAccess.cs
```

Start here for parsing environment files or decoding environment information returned by systemd.
`SystemdServiceEnvironmentReader` is the application-facing adapter. It uses one
`SystemdEnvironmentOperationContext` across identity/protection, acquisition,
parsing, composition, final validation, cleanup and publication.
`SystemdEnvironmentSourceReader` and `LinuxSystemdSourceFileAccess` form the
internal disposable acquisition session and safe Linux file boundary; the
session retains transport and filesystem observations while raw buffers transfer
to parsing/composition. `SystemdEnvironmentComposer` remains the internal I/O-free
composition boundary. The adapter is not registered in Web or Agent and exposes
no public value-reveal operation.

#### Protected services

```text
BuiltInProtectedServices.cs
ServalPrivilegedUnits.cs
```

Start here for built-in protected-service definitions and Serval's own privileged unit exclusions.

Corresponding tests:

```text
tests/Serval.Systemd.Tests/
```

Real-systemd and D-Bus integration tests are also located in this project.

---

### `src/Serval.Infrastructure`

General infrastructure project.

There is currently no substantive implementation in this module.

Use this project for non-systemd infrastructure implementations that satisfy application-facing abstractions.

Corresponding test project:

```text
tests/Serval.Infrastructure.Tests/
```

---

### `src/Serval.Web`

ASP.NET Core Razor Pages host.

Current structure:

```text
src/Serval.Web/
├── Program.cs
├── Pages/
├── wwwroot/
├── appsettings.json
└── appsettings.Development.json
```

`Program.cs` is the application startup/composition entry point.

`Pages/` contains Razor Pages and their page models.

`wwwroot/` contains static web assets. Vendored libraries under `wwwroot/lib/` normally do not need to be inspected during application work.

The project is currently close to the default Razor Pages scaffold and does not yet contain the main Serval UI.

---

### `src/Serval.Agent`

Privileged process boundary.

Current entry point:

```text
src/Serval.Agent/Program.cs
```

The Agent is currently inert and does not yet expose privileged operations or IPC.

Future privileged execution, IPC hosting, and Agent-side composition will start in this project.

Before working here, consult the privileged-operation documentation and applicable agent skill.

## Tests

Production projects currently have these corresponding test projects:

| Production project      | Test project                        |
| ----------------------- | ----------------------------------- |
| `Serval.Domain`         | `tests/Serval.Domain.Tests`         |
| `Serval.Application`    | `tests/Serval.Application.Tests`    |
| `Serval.Infrastructure` | `tests/Serval.Infrastructure.Tests` |
| `Serval.Systemd`        | `tests/Serval.Systemd.Tests`        |

`Serval.Web` and `Serval.Agent` do not currently have dedicated test projects.

### systemd integration tests

`tests/Serval.Systemd.Tests` contains both isolated tests and tests against real systemd behavior.

Important locations include:

```text
tests/Serval.Systemd.Tests/
├── DBus/
├── Fixtures/
├── RealSystemdServiceEnumerationTests.cs
├── RealSystemdServiceInspectionTests.cs
├── RealSystemdServiceInventoryTests.cs
├── RealSystemdEnvironmentFileParserTests.cs
├── RealSystemdEnvironmentSourceReaderTests.cs
└── RealSystemdLoadedEnvironmentTests.cs
```

`Fixtures/` contains the disposable Linux/systemd test environment and supporting scripts.

## Documentation and specifications

### `docs/`

Long-lived technical and architectural documentation.

Current documents cover areas including:

* systemd discovery,
* D-Bus transport,
* service enumeration and inspection,
* service identity,
* environment handling,
* protected services.

Consult the relevant document before changing an established technical strategy.

### `openspec/specs/`

Accepted behavioral specifications for the current system.

Use these when determining what behavior the implementation is expected to provide.

### `openspec/changes/`

Proposed, active, and archived OpenSpec changes.

When implementing an active OpenSpec change, its proposal, specs, design, and tasks define the change-specific implementation context.

## Agent infrastructure

### `.agents/skills/`

Task-specific agent instructions.

Project-specific skills currently include areas such as:

```text
serval-change-workflow/
serval-systemd/
serval-privileged-operation/
```

OpenSpec also installs its workflow skills here.

Load a skill only when the current task matches its trigger as defined in `AGENTS.md`.

### `.agents/workflows/`

Reusable repository workflows.

Current custom workflow:

```text
post-change-review.md
```

### `.agents/agents/`

Custom agent definitions.

Current custom reviewer:

```text
serval-code-reviewer.md
```

## CI

GitHub Actions workflows are under:

```text
.github/workflows/
├── ci.yml
└── codeql.yml
```

Start with `ci.yml` when changing build, test, integration-test, or repository quality-gate behavior.

## Where to start

| Change concerns                      | Start here                                                         |
| ------------------------------------ | ------------------------------------------------------------------ |
| Domain representation of a service   | `src/Serval.Domain/Services/`                                      |
| Application service contracts        | `src/Serval.Application/Services/`                                 |
| Service discovery                    | `src/Serval.Systemd/SystemdServiceEnumerator.cs`                   |
| Service inspection                   | `src/Serval.Systemd/SystemdServiceInspector.cs`                    |
| Application-facing systemd inventory | `src/Serval.Systemd/SystemdServiceInventory.cs`                    |
| D-Bus communication                  | `src/Serval.Systemd/DBus/`                                         |
| Environment-file parsing             | `src/Serval.Systemd/EnvironmentFileParser.cs`                      |
| Loaded systemd environment           | `src/Serval.Systemd/LoadedEnvironmentDecoder.cs`                   |
| Application-facing environment read  | `src/Serval.Systemd/SystemdServiceEnvironmentReader.cs`            |
| Protected service definitions        | `src/Serval.Systemd/BuiltInProtectedServices.cs`                   |
| Web startup                          | `src/Serval.Web/Program.cs`                                        |
| Razor UI                             | `src/Serval.Web/Pages/`                                            |
| Privileged process                   | `src/Serval.Agent/`                                                |
| CI / systemd test environment        | `.github/workflows/ci.yml`, `tests/Serval.Systemd.Tests/Fixtures/` |

## Maintenance rule

Keep this document navigational.

Update it when:

* a project or major implementation area is added, removed, or renamed,
* an important entry point moves,
* responsibility moves between modules,
* a new subsystem becomes significant enough that agents need to know where to find it.

Do not update it for every new class or test.

Prefer directory-level and subsystem-level pointers over exhaustive file listings.
