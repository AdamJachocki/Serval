# Tasks

## 1. Systemd and F5 integration

- [x] 1.1 Extend the development Dockerfile and Compose overrides for root systemd PID 1, private namespaces, ephemeral runtime directories and graceful shutdown; verify manager queries succeed with Docker privileged mode enabled only for disposable local development and no prohibited host mounts.
- [x] 1.2 Add the fixed development debugger bootstrap with identity and capability dropping, direct dotnet debugging and a Web-only stop helper, and override Visual Studio's generated entrypoint/debug settings; verify F5 reaches an executed breakpoint with exactly one Web process under `serval-web`, no retained root capabilities, no manual attachment, and no Shift+F5 error. Stop and revise the design if this integration fails.
- [x] 1.3 Replace background Agent startup with a protected development unit and bounded readiness hook, recreating the socket directory safely under tmpfs; verify one root Agent, correct socket ownership/modes, and startup failure on unsafe paths or missing Agent.
- [x] 1.4 Add repeatable runtime smoke coverage for manager availability, namespace/mount restrictions, debugger launcher identity and shutdown; document the revised startup, accepted development-only privileged mode and compatibility diagnostics in CONTRIBUTING.md and verify its commands against the development container.

## 2. Demonstration baseline and access

- [x] 2.1 Add root-owned running/inactive/failed sample units and a locked unprivileged workload account, with bounded initialization and manager-wide collision checks; verify exact states on real systemd, no sample network listeners, and rejection of existing canonical/alias collisions and symlink destinations.
- [x] 2.2 Extend context copy/ignore allowlists for the development assets; verify staged content is complete, production publish artifacts contain no demos, and no source credentials or secret mounts enter the image.
- [x] 2.3 Add `serval-dev` to the container-local configured administrator group; verify authenticated list/detail reads for each sample and denial for a separate disposable non-admin without grants, unauthenticated requests, malformed/malicious IDs, and protected canonical/alias targets. Exercise Agent directly as well as the UI so Web validation cannot mask boundary failures.
- [x] 2.4 Document the account's container administrator scope, expected sample states and disposable reset procedure in CONTRIBUTING.md; verify fresh recreation restores all states and preserves the existing development login.

## 3. Readiness and failure coverage

- [x] 3.1 Extend health/full verification with bounded manager and sample checks; verify the intentional failed sample is accepted while missing/wrong-state samples, manager timeout and Agent failure are rejected without exposing environment values or credentials.
- [x] 3.2 Add a disposable real-systemd smoke runner and connect automated coverage to the appropriate Linux CI job; verify cleanup after both success and injected failure and record the runtime configuration used. Keep the existing systemd fixture matrix separate.
- [x] 3.3 Update bounded diagnostics and recovery documentation alongside the verifier; verify health checks do not mutate sample states and error output identifies the failed check without broad log or environment dumps.

## 4. End-to-end acceptance and review

- [x] 4.1 Run Visual Studio F5 login, list/detail inspection of all samples, an executed breakpoint and Shift+F5; compare UI states with direct systemd queries and confirm one Web/Agent process and isolation in the resolved generated Compose configuration.
- [x] 4.2 Run applicable formatting, build, unit and real Linux/systemd integration checks with repository quality settings; record each as passed, failed or unverified, keeping Windows, Linux and manual Visual Studio results distinct.
- [x] 4.3 Check the complete task diff against all spec scenarios and obtain a fresh independent review under `.agents/workflows/post-change-review.md`; resolve blockers and majors and require the latest verdict to be APPROVED with mandatory gates passed before completion.
