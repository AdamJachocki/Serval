# Serval

Serval is a security-first, open-source Linux administration application built
as production-grade software. Its long-term product scope is deliberately
narrow: manage environment variables for system-level systemd services and
restart those services only through a separate, explicit action.

> [!IMPORTANT]
> Serval is in active development and has no supported release yet. The
> repository contains working security boundaries and Linux integration, but
> the deployment files are not a supported production installer or release.

## Current implementation

The repository currently implements:

- typed systemd D-Bus discovery and read-only inspection of system services;
- protected-service and Serval privileged-unit exclusion;
- bounded parsing, source acquisition and composition for supported systemd
  service environment declarations;
- a privileged `Serval.Agent` and unprivileged `Serval.Web` connected through
  a fixed local Unix Domain Socket protocol;
- Linux PAM authentication, Agent-owned in-memory sessions and secure
  session-only browser cookies;
- Agent-enforced `Service.View` grants tied to current Linux user and group
  identities;
- root-owned SQLite grant and audit persistence;
- Razor Pages for login, logout, authorized service inventory and inspection,
  and host-administrator grant management;
- managed tests plus disposable real-systemd integration tests across the
  supported systemd 249, 255, 257 and 259 baselines on x64 and ARM64.

Environment values are not exposed through the current Web-to-Agent protocol
or UI. Editing service environment configuration and service lifecycle actions,
including restart, are not implemented yet.

## Security model

Serval enforces a process boundary:

```text
Browser -> Serval.Web (unprivileged) -> local IPC -> Serval.Agent (root)
                                                      |
                                                      +-> systemd
                                                      +-> PAM
                                                      +-> root-owned policy data
```

- `Serval.Web` runs as the dedicated unprivileged `serval-web` account.
- The Agent authenticates the socket peer from kernel credentials and accepts
  only seven bounded typed operations.
- The Agent derives the delegated user from its own session state and performs
  authorization again for every request.
- Caller-supplied usernames, roles, groups and authorization flags are never
  authority.
- Protected services and Serval's privileged units remain hidden and denied.
- Environment values and credentials are treated as secrets and excluded from
  logs, audit records and IPC responses.
- Service reads do not start, stop, restart, reload or modify units.

See [ARCHITECTURE.md](ARCHITECTURE.md), [SECURITY.md](SECURITY.md), and
[the identity deployment guide](docs/identity-deployment.md) for the complete
boundaries and operational constraints.

## Platform and prerequisites

The runtime target is Linux with a system-level systemd manager. The supported
integration-test baselines are Ubuntu 22.04, 24.04 and 26.04 and Debian 13 on
x64 and ARM64. The minimum tested systemd major version is 249.

Development requires:

- the .NET 10 SDK selected by [global.json](global.json);
- Git.

The managed projects and tests can run on other .NET-supported development
hosts. Linux, PAM, filesystem-permission and real-systemd tests run only in a
suitable Linux environment and otherwise report explicit skips.

## Build and test

```bash
dotnet restore Serval.slnx --locked-mode
dotnet format Serval.slnx --verify-no-changes --no-restore
dotnet build Serval.slnx --configuration Release --no-restore
dotnet test --solution Serval.slnx --configuration Release --no-build --no-restore
```

Package lock files are committed so clean checkouts and CI resolve the reviewed
dependency graph. GitHub Actions also publishes self-contained Linux test
artifacts and runs the disposable real-systemd matrix, including cleanup after
an intentionally failing harness run.

## Deployment artifacts

The [deploy](deploy) directory contains the current PAM configuration, systemd
units and identity setup script used to verify the Web/Agent trust boundary.
The manual deployment procedure and required ownership and permissions are
documented in [docs/identity-deployment.md](docs/identity-deployment.md).

These files support development and integration testing. They do not constitute
a versioned installer or supported release.

## Repository documentation

- [ARCHITECTURE.md](ARCHITECTURE.md) describes runtime boundaries and dependency
  direction.
- [SECURITY.md](SECURITY.md) defines security expectations and vulnerability
  reporting.
- [docs/code-map.md](docs/code-map.md) maps the current implementation.
- [docs](docs) contains long-lived technical and deployment decisions.
- [openspec/specs](openspec/specs) contains the accepted behavioral contracts.
- [openspec/changes](openspec/changes) contains active and archived changes.

## Contributing

Read [AGENTS.md](AGENTS.md) before changing the repository. It routes sensitive
work to the required project workflows. See [CONTRIBUTING.md](CONTRIBUTING.md)
for the development process and quality gates.

## License

Serval is licensed under the [Apache License 2.0](LICENSE).
