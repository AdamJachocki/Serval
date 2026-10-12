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
- Treat all environment-variable values and credentials as secrets. Never place them in source, test output, snapshots, logs, exceptions, telemetry, SQLite, issues, or pull requests.
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
