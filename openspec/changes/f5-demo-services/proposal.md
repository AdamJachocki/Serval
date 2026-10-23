# Proposal

## Why

The Visual Studio F5 container currently supports login and Web debugging but does not run systemd or provide sample services, so it cannot demonstrate the service browsing experience before deployment to a target machine. Developers need a reproducible, disposable environment where the UI reads actual service states through the normal Agent boundary.

## What Changes

- Extend the existing one-container F5 profile to run a real system-level systemd manager while preserving automatic Web debugging and one-action startup.
- Include small, clearly named demonstration services with deterministic initial states: running, inactive, and failed. Their workloads are synthetic, but their units, processes, and reported states are real; installing unrelated application servers is unnecessary.
- Make these services visible to the existing disposable development account through the existing authorization model, with no production authorization bypass or new privileged IPC operation. The design will specify the development-only account provisioning needed for this access.
- Initialize the sample states automatically in a fresh disposable container and document how to recreate that baseline. An intentionally failed sample must not make an otherwise ready development environment fail its readiness check; an unavailable systemd manager or incorrectly initialized samples must produce an actionable startup failure.
- Enable Docker privileged mode only for the disposable local F5 development profile so systemd can manage its private cgroup hierarchy. Preserve private namespaces, the loopback Web port, prohibited host mounts and production separation; document the accepted development-only privilege trade-off.
- Update the F5 documentation and verification to cover browser-visible samples, actual systemd states, successful breakpoint attachment, and protected-service exclusion.

Non-goals: implementing service start/stop/restart or environment editing in the UI, changing production deployment, replacing the separate systemd integration fixtures, adding third-party server products, or simulating service data in Web or Agent.

## Capabilities

### New Capabilities

None. The sample environment is part of the existing F5 development workflow.

### Modified Capabilities

- `docker-visual-studio-web-debug`: Extend F5 readiness to include a real systemd manager and reproducible demonstration services accessible through the authenticated UI, while retaining the existing debugging and isolation requirements.

## Impact

- Development image, Compose configuration, startup/context preparation, and verification under `eng/visual-studio-compose/`.
- Development-only service units and minimal workloads, plus development account authorization provisioning using the existing `authorization/system-service-access` contract.
- The Visual Studio F5 section of CONTRIBUTING.md and applicable development-profile tests or smoke checks.
- Security-sensitive runtime integration: systemd startup, container privilege requirements, and Agent readiness must be designed against ARCHITECTURE.md and SECURITY.md. Production IPC, permission semantics, protected-service policy, and deployment artifacts remain unchanged.
- Verification must include real Linux/systemd state checks and a Visual Studio F5 browser/debugger check; login or container health alone does not demonstrate the requested experience.
