# Contributing to Serval

Thank you for helping build Serval. Contributions are expected to meet production-grade quality and security standards even while the project is under active development.

## Before you start

1. Read [`AGENTS.md`](AGENTS.md) in full.
2. Establish the task scope from the relevant issue, OpenSpec change, or direct
   user request.
3. Load and follow skills required by task before working especially across a sensitive boundary.
4. Report vulnerabilities privately according to [`SECURITY.md`](SECURITY.md); do not disclose them in a public issue.

Do not weaken a security invariant to simplify implementation. If a proposed change requires a new privileged capability, its minimal contract, trust basis, threats, failure behavior, and negative tests must be designed before implementation.

## Branch workflow

Before modifying any file:

1. Check the current branch and worktree. Preserve unrelated changes; do not
   switch branches if doing so would carry them into the new work.
2. Update the local `main` from `origin/main`:

```bash
git switch main
git pull
```
3. Create and switch to a dedicated branch from the updated local `main`,
   without inheriting `origin/main` as its upstream:

```bash
git checkout -b <branch-name>
```
4. Remain on the new branch throughout the
   implementation. If an existing feature branch tracks `origin/main`, stop and
   correct its upstream before continuing.

Do not implement directly on `main`. Do not discard or overwrite unrelated
local changes to switch branches.

## Change expectations

- Keep pull requests focused and explain the security impact.
- Preserve the dependency direction documented in [`ARCHITECTURE.md`](ARCHITECTURE.md).
- Do not add speculative abstractions, packages, or project references.
- Treat all production, operator-provided and dynamically generated credentials as secrets. Never place them in source, test output, snapshots, logs, exceptions, telemetry, SQLite, issues, or pull requests. A fixed credential may appear only when an accepted specification explicitly defines it as public test data for a disposable local-development fixture; keep it out of every production artifact and label it prominently where developers use it.
- Add tests that demonstrate meaningful behavior. Security-sensitive and privileged changes require permitted and denied paths plus applicable malicious-input cases.
- Critical systemd behavior must eventually be verified on real Linux/systemd in CI; mocks cannot be the only evidence.
- Update documentation when behavior, trust boundaries, operator expectations, or security assumptions change.

## Pull requests

Complete the pull request template and identify:

- the problem and the intentionally excluded scope;
- architectural or trust-boundary effects;
- validation performed locally;
- follow-up work that should remain separate.

All CI checks must pass with warnings treated as errors. Maintainers may request additional negative-path, integration, or platform-specific testing proportional to risk.

## Visual Studio F5 development profile

The `Serval.VisualStudioCompose` startup project provides the supported
one-container F5 workflow for the complete application. It requires Visual
Studio with Container Tools, PowerShell 7, and Docker Desktop in Linux-container
mode (or another Linux Docker Engine reachable by Visual Studio). Docker Compose
must support lifecycle hooks.

Before starting, select `Serval.VisualStudioCompose` as the startup project and
select its `Serval Docker Web` profile. This is a development-only Compose
runtime: it runs systemd as PID 1 and does not replace the separate systemd
fixtures. Set a breakpoint in an executed no-login Web path, such as
`LoginModel.OnGet`, then press F5. One action builds the
allowlisted context, starts the single `serval` Compose service, starts
the protected Serval.Agent systemd unit through the fixed readiness hook, launches Serval.Web as
`serval-web` through a debugger bootstrap that drops groups and all capabilities, and opens
`http://localhost:5123/Login`.

The image contains the public disposable Linux account `serval-dev` with
password `serval`. It belongs to the container-local `serval-admin` group and can
inspect every non-protected container service through the existing Agent policy.
It cannot inspect the protected Agent, even through aliases. Use it only in this
disposable local development container. It is
not a production credential, is not configurable as a production default, and
must never be copied into deployment scripts, systemd units, production images,
or operator documentation. The technical `serval-web` account remains locked
and cannot be used for interactive login.

Every fresh container automatically installs and initializes these real systemd
services. Their locked `serval-demo` workload account has no capabilities or
network listeners; the units are development assets excluded from production
publishing.

| Service | Expected active state | Workload |
| --- | --- | --- |
| `serval-demo-running.service` | `active` | Sleeps until stopped |
| `serval-demo-inactive.service` | `inactive` | Installed, not started |
| `serval-demo-failed.service` | `failed` | Intentionally exits with status 1 |

After login, open Services and inspect each sample. The failed sample is expected
and may make the manager report `degraded`; readiness checks the individual states
and manager responsiveness. Health checks never restart or repair samples.

While F5 is active, identify the Visual Studio container and run the bounded
in-container verification:

```powershell
$container = docker ps --filter "label=com.microsoft.visual-studio.project-name=Serval.Web" --quiet
if (@($container).Count -ne 1) { throw "Expected exactly one Visual Studio Serval container." }
docker exec $container /opt/serval/development/verify-development-host.sh
docker inspect $container --format 'Privileged={{.HostConfig.Privileged}} PidMode={{json .HostConfig.PidMode}} IpcMode={{json .HostConfig.IpcMode}} CgroupnsMode={{json .HostConfig.CgroupnsMode}}'
docker inspect $container --format 'Ports={{json .NetworkSettings.Ports}}'
docker inspect $container --format '{{range .Mounts}}Type={{.Type}} Source={{.Source}} Destination={{.Destination}} RW={{.RW}}{{println}}{{end}}'
```

The verifier requires a responding systemd manager, all three expected sample
states, exactly one root Agent, exactly one Web process owned by
`serval-web` with no retained capabilities, correct local socket permissions, a responding login page, and its rendered
stylesheets. In the narrowly formatted inspection output, verify that the
container is privileged only for disposable local development, uses private IPC and cgroup namespaces, has no
host PID namespace or Docker socket mount, and publishes only container port
8080 to host loopback port 5123. Do not use an unrestricted `docker inspect` in
diagnostics because it includes environment-variable values, which must be
treated as sensitive.
Normal Visual Studio development mounts include the source/build context,
debugger and Hot Reload tools, NuGet caches, development HTTPS certificates,
and user-secrets directories. Certificate and user-secret mounts must be
read-only; their contents must never be copied into the image or diagnostics.
Privileged mode is an accepted exception for this disposable local profile:
systemd needs a writable private cgroup hierarchy. It increases access to engine
devices and mounted development files; read-only mounts still permit reading.
Never mount the host cgroup tree, systemd bus, Docker socket or account files.
This exception does not apply to production deployment.

If startup fails, inspect the Visual Studio Build and Container Tools output,
the generated Compose override under
`eng/visual-studio-compose/obj/Docker/`, the container health status, and the
bounded Agent unit status inside the disposable container:

```powershell
docker inspect $container --format '{{json .State.Health}}'
docker exec $container timeout 5s systemctl show serval-agent.service --property=ActiveState,SubState,Result,ExecMainStatus
docker exec $container timeout 5s systemctl show serval-demo-running.service serval-demo-inactive.service serval-demo-failed.service --property=Id,LoadState,ActiveState,SubState
```

The command prints only unit state and exit status, without environment or log contents.
The startup hook has a 90-second overall limit. A manager error indicates that
PID 1 or private writable cgroups are unavailable; check the development Compose
configuration and Docker Linux-container support. A sample collision or unsafe
path is rejected rather than overwritten. A wrong sample state is reported by
name; recreate the disposable container to recover its initial baseline.

An Agent exit, unsafe runtime path, or IPC timeout fails the
root lifecycle hook with a specific message. A missing or duplicate Web process,
wrong kernel UID, unavailable page, or missing static asset fails
`verify-development-host.sh`. If the page loads but the selected breakpoint is
not reached, treat F5 as failed: confirm that the generated override selects
`/usr/bin/dotnet`, `/opt/serval/web/Serval.Web.dll` and `/opt/serval/web` before retrying. The read-only debugger entrypoint overlay must select the fixed bootstrap, which starts vsdbg as `serval-web`. The stop command must select `/opt/serval/development/stop-web.sh`.
Do not attach manually or accept a browser-only success.

Stop debugging with Shift+F5. If Visual Studio leaves the disposable project
running, remove only that Compose project through Visual Studio's Containers
window or its generated Compose project; do not reuse this profile as a
production deployment or systemd test. Production publishing, PAM deployment,
and systemd units remain unchanged and are verified by their separate workflows.

To reset, use the Containers window to remove only this profile's container after
Shift+F5, then press F5 again. Removing and recreating the container restores all
three states and the disposable account. Merely stopping and starting the same
container does not constitute a fresh baseline.

For automated runtime, authorization and negative-path checks without Visual
Studio, run `pwsh -NoProfile -File eng/visual-studio-compose/test-development-host.ps1`.
It creates its own uniquely named container without host mounts or published
ports and removes it and its image on success or failure. Use
`-InjectFailureAfterStart` to verify failure cleanup (an intentional nonzero
result). This separate CI smoke checks real systemd, direct Agent requests and
rendered HTTP pages; actual F5 breakpoint and Shift+F5 acceptance remains a Visual
Studio check.

## Environment browser verification

The environment browser tests use an installed Node.js/Playwright runtime and
Chromium. Set `NODE_PATH` when Playwright is supplied outside this repository;
optionally set `SERVAL_BROWSER_EXECUTABLE` to the installed Chromium executable.
No browser dependency is included in production publishing.

Set `SERVAL_BROWSER_TESTS=1`, then run
`dotnet test --project tests/Serval.Web.Tests`. This starts a local HTTPS Kestrel
test host with an ephemeral test certificate and a typed fixture client. It
checks single-row reveal, inert multiline text, manual hiding, no timer, fresh
reveal requests, canceled late responses, refresh/history reset, keyboard input
and a narrow viewport. Without the opt-in, the browser test is explicitly skipped.

For the complete real-systemd path, run
`node tests/Serval.Web.Tests/browser/real-systemd.cjs` with Docker Desktop in
Linux-container mode. The runner builds the existing disposable development
image, creates a unique privileged container with private namespaces and no host
mounts, and publishes one random port on host loopback. It seeds private generated
environment values before the product observation window and uses the documented
public disposable account. It verifies exact one-value disclosure, value-free
metadata/audit, CSRF and protected-target denials, unchanged source bytes and
service state. The runner removes its container, image and temporary database
on exit. It never records screenshots, traces or value-bearing diagnostics.

These local checks do not replace the supported real-systemd CI matrix. The
existing systemd contract harness also exercises the Agent environment operations
against private manifests and its file/lifecycle/reload observers.

## Debian 13 systemd Compose smoke test

Use Docker Desktop in Linux-container mode (or a Linux Docker Engine), Docker Compose v2, and PowerShell 7. From the repository root, run:

```powershell
pwsh -NoProfile -File tests/Serval.Systemd.Tests/Fixtures/test-compose-systemd.ps1
```

The script builds the existing Debian 13 CI Dockerfile, starts systemd as PID 1,
waits up to 90 seconds for a healthcheck that queries the live manager, queries
`systemctl` again, then asks Compose to stop the container within 30 seconds.
It expects a running, healthy container before stop and exit code 0 or 130
afterward; Docker Desktop has been observed to report 130 for this systemd
shutdown signal. A forced kill (137) and an OOM kill fail the test.
It always runs `docker compose down --remove-orphans`; on failure it saves the
last 100 container log lines to an ignored `Fixtures/artifacts/` file and
prints that file's path, without echoing log content to CI output.

The Compose fixture has no host bind mounts, named volumes, published ports,
Docker socket, Linux account or PAM mounts. Its build context sends only the
existing Dockerfile. Its privileged container setting is for this disposable systemd test
only. The separate real-systemd contract matrix still uses its own CI harness.

## Disposable Agent PAM Docker fixture

Use Docker Desktop in Linux-container mode (or a Linux Docker Engine), PowerShell
7 and the repository's .NET SDK. From the repository root run:

```powershell
pwsh -NoProfile -File tests/Serval.Agent.Tests/Fixtures/test-docker-agent-pam.ps1
```

The runner publishes the Linux Agent and a small typed IPC client using locked
dependencies, stages only those outputs and the deployed PAM/unit files, and
builds a separate Debian 13 image. It runs systemd and the root Agent in a
disposable container without host mounts or published ports. After startup it
creates local login accounts and generates a new password in memory, passing it
only through Docker's stdin pipe to `chpasswd` and the test client. The password
is never supplied as an argument, environment variable or build input. The
runner prints result categories, never credentials or session tokens.

It checks the socket owner and modes, Web peer UID, PAM success and denials, and
fail-closed startup for unsafe deployment state. A passing run prints
`Agent Docker PAM fixture passed.` It removes its own container, images and
temporary build context even on failure. To verify failure cleanup locally,
run the same command with `-InjectFailureAfterStart`; that invocation must fail
after container creation, and no `serval-agent-pam-*` container should remain.
