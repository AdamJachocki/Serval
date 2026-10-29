# Design

## Context

See proposal.md for motivation. The existing Dockerfile has no systemd. The debug Compose override runs as `serval-web`, starts Agent through a root `post_start` hook, and selects `dotnet` as the debuggee. Visual Studio's generated override replaces the entrypoint with DistrolessHelper. The context preparation script copies an explicit allowlist. Docker privileged mode is explicitly accepted for this disposable local development profile, as it is for the separate systemd fixture; production deployment remains outside this exception.

## Goals / Non-Goals

**Goals:** Preserve one-container F5 and the existing application boundary while providing actual manager-backed sample metadata. Keep initialization deterministic and verification bounded.

**Non-Goals:** No production module changes, new IPC capabilities, direct policy database seeding, environment fixtures, or UI lifecycle operations. Existing authorization and read contracts remain authoritative.

## Decisions

### Runtime and debugger ownership

Run systemd as PID 1 with root container startup, private namespaces, ephemeral `/run` and `/run/lock`, and graceful systemd shutdown. Explicitly override the generated Visual Studio entrypoint. Enable Docker privileged mode only in the disposable local F5 development configuration, retaining the engine-provided private cgroup namespace and prohibiting host cgroup/bus, Docker socket and host account mounts. A non-privileged probe failed because systemd could not create `/init.scope` on the read-only cgroup filesystem. A privileged probe on the installed Docker Desktop successfully answered manager queries with a writable private cgroup hierarchy and no host mounts; this establishes runtime feasibility, not F5 debugger acceptance.

Use a development-only fixed debugger bootstrap which drops supplementary groups, GID, UID and all capabilities to `serval-web` before starting vsdbg. Visual Studio launches the fixed dotnet Web command directly under that debugger. Overlay its expected debugger entrypoint with a read-only development shim and mount the unchanged Microsoft debugger separately read-only. Accept only the fixed interpreter argument and fixed architecture-specific debugger locations. No host debugger files are edited. Before dropping identity, validate that the fixed /dev/console is a root-owned character device and not a symlink, then assign its group to serval-web with mode 0620. This permits debugger output to the private container TTY without console read access; reject substituted paths. Verify write-only access and rejection of unsafe console paths. A fixed stop helper drops to the Web identity before signalling only the exact Web command; absence is idempotent success. Preserve portable static asset arguments. A root Web process, manual attach, an unprocessed breakpoint or a Shift+F5 error fails acceptance. The earlier shell debuggee that changed identity before exec did not pass the Visual Studio test and is no longer the debugger target.

This changes the documented direct-dotnet launcher detail in CONTRIBUTING.md, not the accepted requirement to automatically debug exactly one unprivileged Web process. First implement and verify this narrow startup path before adding samples. The chosen integration is not yet proven on the installed Visual Studio/Docker combination; a failed compatibility check blocks implementation completion and requires revisiting this design, not switching silently to mocks or adding host mounts.

### Agent and sample lifecycle

Use a development-only protected `serval-agent.service` to supervise the existing Agent binary as root. Replace the background `setsid` startup path with a bounded readiness hook; never launch Agent twice. Account for tmpfs hiding the image's `/run/serval` by creating the socket directory with the existing ownership/mode at runtime. Keep production units unchanged.

Package three root-owned demo units named `serval-demo-running.service`, `serval-demo-inactive.service`, and `serval-demo-failed.service`. Use a dedicated locked unprivileged workload identity. The running workload is a long-running sleep, the inactive unit is installed but not started, and the failed unit executes a fixed nonzero-exit command with automatic restart disabled. No network ports or environment data are needed. Inspect protected-policy matching to ensure these ordinary demo names remain visible while the Agent unit remains protected.

Initialize once per fresh container using fixed unit names and direct executable arguments. Before installing development fixture units, reject collisions with existing manager-visible canonical names or aliases; do not shadow vendor or privileged units. Starting the failed sample is intentionally unsuccessful: verify its resulting state explicitly rather than ignoring arbitrary initialization errors. Do not connect its failure to Agent through a required-success dependency.

### Account provisioning

Add the existing disposable `serval-dev` account to the existing container-local `serval-admin` group in the development image. This uses current administrator bootstrap semantics and gives access to all non-protected container services, not only samples. Document that scope. Do not add sudo rights, make `serval-web` an administrator, seed SQLite, or change Agent authorization. A separate disposable non-admin identity without grants is used by negative tests only.

### Readiness and repeatability

Poll manager, Agent socket/process and exact expected sample states for at most 90 seconds, with bounded individual queries. Check the live manager rather than requiring global system state `running`, since the intentional failed unit can produce `degraded`. Health mode checks manager, Agent and samples; full verification also checks exactly one unprivileged Web process, login page and CSS. Neither mode repairs service state. Browser inspection separately establishes that authenticated service listing/detail pages match systemd.

Recreating only this Compose project restores the baseline. Do not persist demo state in a named volume. Add all new files explicitly to the build-context copy and ignore allowlists. Keep outputs limited to unit identities, states and failure categories; never dump environment, credentials or session cookies.

### Privileged boundary analysis

New root work is confined to trusted image construction and container initialization: installing fixed demo units, assigning a local group, starting systemd and Agent. There is no browser-triggered root capability and no new request/response contract. Existing attacker-controlled login credentials, service IDs, aliases and IPC fields remain checked by Agent against PAM/NSS identity, grants and protected policy. Fixed root-owned scripts and paths receive no browser values; no generic command runner or request-selected filesystem path is added. Root setup must reject unsafe ownership, symlink destinations and collisions rather than overwrite them. Existing Agent audit and fail-closed behavior apply unchanged.

## Risks / Trade-offs

- Systemd/private-cgroup support or debugger process following may fail on the target tooling → perform the runtime/debugger acceptance check first; do not claim readiness from static configuration.
- Docker privileged mode and root debugger tooling increase development-container exposure, including access to engine devices and mounted development files. This is an accepted exception only for disposable local development, not a production isolation guarantee. Retain private namespaces, loopback port and read-only secret/certificate mounts; no host manager resources. Document this scope and verify the resolved Visual Studio mounts; read-only mounts prevent writes but do not prevent privileged processes from reading their contents.
- Administrator development account sees additional non-protected units → document this and test denial for a separate non-admin plus protected canonical/alias targets.
- Expected failed sample makes the manager degraded → compare exact fixture states and manager responsiveness instead of global success alone.

## Migration Plan

Rebuild the development image and recreate the disposable Compose project. Update CONTRIBUTING.md with startup architecture, account scope, expected states, bounded diagnostics and reset instructions; these are the canonical operational notes. No production or database migration applies. Rollback restores the former development configuration and recreates only its container.

Implementation verification includes container isolation inspection, real systemd state checks, positive and denied Agent reads, failure injection, clean recreation, and Visual Studio F5/breakpoint/Shift+F5 verification. Windows unit tests cannot substitute for those runtime checks.
