# Serval

Serval is a security-first, open-source Linux administration application. It is being built as production-grade software, not as a proof of concept.

The initial product scope is deliberately narrow: manage environment variables for system-level systemd services and restart those services only through a separate, explicit action.

> [!IMPORTANT]
> Serval is in early development and has no supported release yet. The current repository contains bootstrap scaffolding only and must not be deployed as an administration service.

## Security model

Serval is designed around a strict process boundary:

```text
Browser -> Serval.Web (unprivileged) -> local IPC -> Serval.Agent (privileged)
```

The privileged boundary and IPC are not implemented in this bootstrap. Future work must preserve the invariants in [`AGENTS.md`](AGENTS.md), including:

- the network-facing process never runs as root;
- privileged operations remain narrow, typed, independently authorized, and auditable;
- environment-variable values are always treated as secrets;
- protected services cannot be enabled through the web application;
- Serval never modifies vendor-owned or user-owned systemd unit files directly;
- saving configuration and restarting a service remain separate operations.

See [`docs/architecture.md`](docs/architecture.md) for the module boundaries and intended dependency direction.

## Repository status

This bootstrap includes:

- a .NET 10 solution with the planned modular-monolith projects;
- a framework-generated Razor Pages skeleton and an inert Agent executable;
- centralized build and package configuration;
- empty xUnit test projects ready for meaningful tests;
- CI, CodeQL, Dependabot, and GitHub contribution templates.

It intentionally does not implement systemd, PAM, authorization, persistence, IPC, service discovery, environment management, restart behavior, packaging, or privileged operations.

## Prerequisites

- A compatible .NET 10 SDK selected by [`global.json`](global.json)
- Git

Linux is the target platform. The bootstrap can also be built and tested on other .NET-supported development hosts because it contains no Linux integration yet.

## Build and test

```bash
dotnet restore --locked-mode
dotnet format Serval.slnx --verify-no-changes --no-restore
dotnet build Serval.slnx --configuration Release --no-restore
dotnet test --solution Serval.slnx --configuration Release --no-build --no-restore
```

Package lock files are committed so clean checkouts and CI resolve the reviewed dependency graph.

## Contributing

Read [`AGENTS.md`](AGENTS.md) before changing the repository. It contains mandatory security invariants and routes sensitive work to project-specific skills. See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the development workflow and [`SECURITY.md`](SECURITY.md) for private vulnerability reporting.

## License

Serval is licensed under the [Apache License 2.0](LICENSE).
