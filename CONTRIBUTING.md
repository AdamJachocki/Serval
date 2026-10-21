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
runtime: it does not run systemd and does not replace the separate systemd
fixtures. Set a breakpoint in an executed no-login Web path, such as
`LoginModel.OnGet`, then press F5. One action builds the
allowlisted context, starts the single `serval` Compose service, starts
Serval.Agent as root through the fixed lifecycle hook, launches Serval.Web as
`serval-web`, attaches the managed debugger directly to `dotnet`, and opens
`http://localhost:5123/Login`.

The image contains the public disposable Linux account `serval-dev` with
password `serval`. Use it only to exercise the local F5 browser login flow. It is
not a production credential, is not configurable as a production default, and
must never be copied into deployment scripts, systemd units, production images,
or operator documentation. The technical `serval-web` account remains locked
and cannot be used for interactive login.

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

The verifier requires exactly one root Agent, exactly one Web process owned by
`serval-web`, the local Agent socket, a responding login page, and its rendered
stylesheets. In the narrowly formatted inspection output, verify that the
container is not privileged, uses private IPC and cgroup namespaces, has no
host PID namespace or Docker socket mount, and publishes only container port
8080 to host loopback port 5123. Do not use an unrestricted `docker inspect` in
diagnostics because it includes environment-variable values, which must be
treated as sensitive.
Normal Visual Studio development mounts include the source/build context,
debugger and Hot Reload tools, NuGet caches, development HTTPS certificates,
and user-secrets directories. Certificate and user-secret mounts must be
read-only; their contents must never be copied into the image or diagnostics.

If startup fails, inspect the Visual Studio Build and Container Tools output,
the generated Compose override under
`eng/visual-studio-compose/obj/Docker/`, the container health status, and the
bounded Agent log inside the disposable container:

```powershell
docker inspect $container --format '{{json .State.Health}}'
docker exec $container sh -c 'tail -n 200 /tmp/serval-agent.log'
```

The Agent-log command is deliberately limited to its last 200 lines.

An Agent exit, duplicate Agent, pre-existing socket, or IPC timeout fails the
root lifecycle hook with a specific message. A missing or duplicate Web process,
wrong kernel UID, unavailable page, or missing static asset fails
`verify-development-host.sh`. If the page loads but the selected breakpoint is
not reached, treat F5 as failed: confirm that the generated override selects
`dotnet`, `/opt/serval/web/Serval.Web.dll`, and `/opt/serval/web` before retrying.
Do not attach manually or accept a browser-only success.

Stop debugging with Shift+F5. If Visual Studio leaves the disposable project
running, remove only that Compose project through Visual Studio's Containers
window or its generated Compose project; do not reuse this profile as a
production deployment or systemd test. Production publishing, PAM deployment,
and systemd units remain unchanged and are verified by their separate workflows.

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
