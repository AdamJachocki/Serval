## 1. One-container development runtime

- [x] 1.1 Build an allowlisted development image with the required .NET runtime, Agent output, locked `serval-web` account, state and IPC directories, and no production systemd units or production credentials; verify the image contents and framework-dependent Web runtime availability.
- [x] 1.2 Add bounded development support scripts that validate prerequisites, start exactly one Agent as root, wait for its local socket, and report process, identity, IPC, or Web readiness failures during F5.
- [x] 1.3 Replace the systemd Compose fixture with one disposable unprivileged application service exposing only the Web loopback port; verify `docker compose config` and confirm container startup, both process identities, IPC readiness, Web response, and clean shutdown during F5.
- [x] 1.4 Add the public development-only `serval-dev` / `serval` Linux account to the debug image; verify its PAM-backed browser login succeeds, an incorrect password is denied, `serval-web` remains locked, and production deployment artifacts remain unchanged.

## 2. Visual Studio F5 workflow

- [ ] 2.1 Configure the Visual Studio Compose profile to run the container and framework-dependent Web command as `serval-web`, start Agent through a root-only Compose lifecycle hook, and debug Web directly through `dotnet`; inspect generated overrides and verify one F5 action starts exactly one Agent and one Web process without manual attachment.
- [ ] 2.2 On a supported Visual Studio and Linux-container Docker setup, set a breakpoint in an executed no-login Web path, press F5, request the path, and record the stopped breakpoint together with the single-container, process, UID, and IPC checks.
- [ ] 2.3 Make startup-hook and profile failures emit bounded actionable diagnostics for Agent exit, IPC timeout, Web launch failure, or missing breakpoint attachment; exercise reproducible failure paths manually without adding a separate headless runner for this profile.

## 3. Isolation, documentation, and review

- [ ] 3.1 During F5, inspect the resolved Visual Studio Compose profile and running container for mounts, ports, namespaces, process identities, Docker socket exposure, forbidden privileged settings, host-sensitive mounts, root Web, duplicate processes, or a second service.
- [ ] 3.2 Document prerequisites, F5 profile selection, breakpoint path, normal development mounts, diagnostics, inspection, and cleanup in the canonical developer guide; verify each command against the implemented profile and keep testing and production deployment explicitly separate.
- [ ] 3.3 Run relevant formatting and build gates, record the human F5 startup, breakpoint, Web smoke, login, process-identity, IPC, isolation, and failure-path outcomes, and complete the repository's independent post-change review before implementation is considered complete.
