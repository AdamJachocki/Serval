## Context

See `proposal.md` and the new delta spec. The current Visual Studio profiles start only Web, while exercising the complete application requires the local root Agent, the unprivileged Web process, and their Unix Domain Socket. Production uses systemd and self-contained publishes, but those deployment concerns are verified independently and are not part of the interactive F5 workflow. Runtime and trust boundaries remain those in `ARCHITECTURE.md`, `SECURITY.md`, and `docs/identity-deployment.md`.

## Goals / Non-Goals

**Goals:**

- Make one disposable development image and one Compose application service the F5 target.
- Start Agent and Web with one F5 action and automatically debug the sole Web process.
- Preserve the production identity and privilege split: Agent runs as root and Web runs as `serval-web`.
- Provide a disposable container-local account for exercising the real PAM-backed browser login flow.
- Keep startup, process identity, IPC readiness, and debugger attachment easy to inspect and diagnose.

**Non-Goals:**

- Validating systemd units, systemd-as-PID-1 behavior, production lifecycle, or production publishing.
- Running unit or integration tests as part of F5 startup.
- Production container deployment, grant setup, or changes to browser authentication behavior.
- New Agent operations, wider IPC permissions, service-management behavior, or generic privileged process launch.

## Decisions

### Use a dedicated development runtime image

Build a development-only Linux image with the .NET runtime required by framework-dependent Web output, the Agent output, the fixed `serval-web` account, local IPC directories, and narrowly scoped development startup scripts. Visual Studio may mount normal debugger tooling. The image does not install or start systemd, copy production unit files, or change either project's production publish contract. This keeps F5 startup fast and avoids making an interactive debugger responsible for production service supervision.

Create `serval-dev` with password `serval` only inside this disposable development image. It is a deliberately public test credential, not a secret, and authenticates through the same container-local PAM path used by Agent. Keep it out of production installers, units, configuration, and images. The technical `serval-web` identity remains locked and non-interactive.

### Let Visual Studio launch Web directly and start Agent through Compose

Use the normal Visual Studio Compose debugging path with `dotnet` as the debuggee program and the framework-dependent Web assembly as its argument. Run the container's main process and the Visual Studio debuggee as `serval-web`, so the managed debugger attaches to the Web runtime it starts instead of following a script or attaching manually. A development-only Compose `post_start` hook runs a bounded Agent starter as root in the same container; that starter rejects duplicates, starts exactly one Agent, waits with a finite timeout for the expected local socket, and reports failures. The hook grants root only to the Agent startup command and does not make the service privileged. Container teardown ends all remaining processes; this is not a production supervisor.

Visual Studio also injects a development static-web-assets manifest from the Windows build output. Its drive-letter content roots are invalid inside Linux. The debuggee command-line explicitly overrides that host-specific manifest with a fixed missing path inside the image, which makes ASP.NET skip the development manifest, sets Web root to the published `/opt/serval/web/wwwroot`, and uses the portable publish endpoint manifest beside the image assembly. The readiness check follows every stylesheet link rendered by the login page so both startup and fingerprinted asset delivery failures are detected before the profile is accepted.

The interactive proof uses an executed Web path reachable without login, such as `LoginModel.OnGet`. A normal page load without a stopped breakpoint does not satisfy the debugger gate.

### Limit the development container's access

The Compose service does not need a privileged container, host PID/cgroup namespaces, systemd runtime mounts, or the Docker socket. Publish only the development Web port on host loopback. Inspect Visual Studio's resolved profile and runtime mounts; allow only its documented source, build-output, debugger, package-cache, development certificate, and user-secret mounts. Keep the Agent socket and development state inside the disposable container. Development secret mounts must remain read-only and their values must never enter image layers, Compose environment committed to the repository, diagnostics, or logs. The explicitly documented public `serval-dev` test password is not treated as a secret and exists only in the disposable development image.

### Treat human F5 startup and breakpoint attachment as the acceptance gate

Keep the in-container readiness check bounded to process identity, kernel UIDs, the local socket, and Web availability. During the supported human F5 flow, inspect the resolved Compose service and running container for mounts, ports, namespaces, process identities, Docker socket exposure, and duplicate processes. Perform that inspection while the breakpoint is active or after resuming it, and record the supported Visual Studio and Docker versions, breakpoint location, and result. This development profile has no separate headless runner; the interactive breakpoint and inspection are its acceptance gate.

### Privileged boundary analysis

The proposal adds no request or root API. Agent remains the only root Serval process and retains its typed local socket protocol. Browser and Web inputs remain untrusted and are checked by Agent as documented in `SECURITY.md`. The launcher contains only fixed executable paths, identities, and readiness checks; no request controls a command, path, or identity. Web does not gain write access to Agent configuration, policy state, or binaries beyond its existing local IPC access. Failure to establish the expected Agent, socket, or Web identity exits startup; it never falls back to root Web or a broader IPC contract.

## Risks / Trade-offs

- [The Agent hook and Web debug launch have no guaranteed ordering] → The hook waits for Agent IPC before completing, Web tolerates the short startup overlap, and the acceptance gate verifies both processes and the socket before exercising privileged behavior.
- [Agent can exit after Web has started because this launcher is not a service supervisor] → Surface Agent logs and process state in diagnostics; restarting F5 recreates the disposable environment. Production supervision remains systemd's responsibility.
- [Visual Studio generated overrides add development mounts] → Allow only documented read-only or workspace/debug mounts, inspect actual `docker inspect` output, and keep the profile separate from production deployment.
- [Web can start before Agent IPC is ready] → Create fixed development state directories in the image, make the root hook wait for Agent's socket with a finite timeout, and require the readiness check before exercising Agent-backed behavior.
- [Visual Studio F5 is not reproducible on headless CI] → Keep this profile explicitly human-operated and retain a recorded F5 breakpoint and container-inspection gate on the supported developer setup; unit and integration tests remain separate workflows.
- [A fixed credential could be mistaken for production configuration] → Name and document it as a public development-only account, test the production-artifact boundary, and never add it to deployment scripts or units.

## Migration Plan

Add the development profile without changing production units, production publish commands, or existing test fixtures. Run the Visual Studio F5 breakpoint and human container-inspection gate before treating the profile as supported. Remove the profile, image, Agent startup and readiness scripts, and documentation to roll back; production deployment and independent tests remain available.
