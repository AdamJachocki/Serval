## Why

Serval lacks a one-action Visual Studio workflow that starts the complete local application and attaches the debugger to Web. Developers need an F5 environment for exercising and debugging application behavior, independently of production systemd deployment and separately scheduled automated tests.

## What Changes

- Add one development-only container containing Serval.Agent and Serval.Web, started together by a Visual Studio Compose F5 profile.
- Let Visual Studio launch and automatically debug the framework-dependent Web process directly with `dotnet` under the dedicated unprivileged identity, while a development-only Compose lifecycle hook starts Agent as root in the same container.
- Keep Agent-to-Web IPC local to the container and expose only the development Web endpoint on host loopback.
- Add the fixed development-only Linux login `serval-dev` / `serval` so the complete browser authentication flow can be exercised without host accounts.
- Add lightweight in-container readiness checks and document human inspection of both application processes, their identities, IPC availability, and debugger attachment during F5.
- Document F5 startup, debugging, inspection, failure diagnosis, and cleanup.
- Exclude systemd/unit validation, automated unit and integration test execution, production publishing, and production deployment behavior from this development profile.

## Capabilities

### New Capabilities

- `docker-visual-studio-web-debug`: One-container Visual Studio F5 workflow for starting the complete Serval application and debugging Web.

### Modified Capabilities

None. Production deployment, identity, IPC, and authorization requirements remain in force.

## Impact

- Development-only Docker image, disposable login account, Agent startup hook, Compose definition, Visual Studio project/profile configuration, diagnostics, and developer documentation.
- Serval.Web gains development container tooling for framework-dependent debugging; its production publish mode is unchanged.
- Serval.Agent still owns privileged operations and runs as root, while the network-facing Web process remains `serval-web`; no privileged API or production systemd behavior changes.
