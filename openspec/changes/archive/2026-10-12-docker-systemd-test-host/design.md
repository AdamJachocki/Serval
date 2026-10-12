## Context

See `proposal.md`. The Debian CI matrix already builds `tests/Serval.Systemd.Tests/Fixtures/DebianSystemd.Dockerfile` and starts it with `docker run`; that job bind-mounts published binaries and fixture scripts for full real-systemd tests. The new Compose host is a separate startup smoke target and must not inherit those mounts. Existing production boundaries are described in `ARCHITECTURE.md` and `SECURITY.md`; real-systemd test rules are in `.agents/skills/serval-systemd/references/integration-tests.md`.

## Goals / Non-Goals

**Goals:**

- One maintained Debian 13 image definition, reused by the Compose service.
- The same Compose startup readiness probe usable from a Linux CI runner and Docker Desktop with Linux containers.
- Bounded startup and graceful shutdown evidence, with cleanup even after a failed smoke check.
- A reviewable configuration that contains no bind mounts, named volumes, account or PAM material, Docker socket, host networking, or repository test output.

**Non-Goals:**

- Moving the full Debian real-systemd contract test harness to Compose.
- Adding login, PAM, Agent, Web, IPC, production deployment, or mutable systemd fixtures to the smoke image.
- Promising Docker Desktop modes that cannot run Linux containers.

## Decisions

### Reuse the existing image

Add a Compose file under `tests/Serval.Systemd.Tests/Fixtures` whose build context is that fixture directory and whose Dockerfile is the existing `DebianSystemd.Dockerfile`. A fixture-local `.dockerignore` sends only that Dockerfile to Docker, excluding scripts and generated logs. Preserve its exec-form `/sbin/init` command and `STOPSIGNAL SIGRTMIN+3` so systemd remains PID 1 and receives its native shutdown signal. Do not copy host account files or PAM configuration into the image. A separate Dockerfile would drift from the CI baseline.

### Keep the Compose service disposable and self-contained

Use the minimum container settings demonstrated necessary by the current Debian harness: an explicit test-only privilege setting and container-owned tmpfs for `/run` and `/run/lock`. Prefer a private cgroup namespace and the engine-provided cgroup filesystem; do not configure host cgroup bind mounts or other volumes. Confirm this exact configuration on both GitHub's Linux runner and Docker Desktop before accepting it. If one engine cannot give systemd a working cgroup tree under these constraints, adjust only container runtime settings that preserve the no-host-data-mount rule and reverify both platforms; do not add host account, PAM, Docker socket, workspace, or generic host filesystem mounts to make the smoke test pass. No network ports are published.

This container runs systemd with Docker-level privilege solely for test startup. No browser, Serval.Web, Serval.Agent, IPC request, principal, or product authorization path is involved. There are no attacker-controlled unit names, paths, values, or IPC fields in the smoke flow. No new root capability is exposed by Serval. The Compose file and its use must remain under test fixtures, not production deployment guidance.

### Make startup and shutdown observable

Add a container healthcheck that succeeds only when `/proc/1/comm` identifies systemd and a bounded `systemctl` query to the system manager succeeds. `docker compose up --build --detach --wait --wait-timeout <bounded seconds>` becomes the shared startup assertion. Do not treat `systemctl --version` or a socket file alone as evidence that the manager responds. A test runner then issues a second explicit `systemctl` query, checks the container is still running, calls `docker compose stop` with a configured grace period, and checks the resulting exit state for a normal shutdown rather than a forced kill. Accept exit 0 or Docker Desktop's observed 130 after the systemd shutdown signal, but reject exit 137, OOM kills, and runtime errors. Always run `docker compose down --remove-orphans` in cleanup and collect logs on failure without printing credentials or environment values. Document equivalent commands for Docker Desktop's shell.

### Add a focused CI smoke gate

Run the Compose startup/stop check in a dedicated Linux CI job using the same file and command sequence documented for Docker Desktop. Keep the existing x64/ARM64 Debian contract matrix and its current bind-mounted fixture execution unchanged; it continues to prove systemd integration behavior. The new smoke job proves the no-mount Compose host lifecycle. Review `docker compose config` and container mount metadata in the test so an accidental host-data mount fails the gate.

## Risks / Trade-offs

- [Docker Desktop cgroup behavior differs from hosted Linux] -> Verify the selected Compose cgroup configuration on both engines during implementation; report a failing platform instead of silently adding a host bind mount.
- [Container privilege gives broad access to the Docker daemon's Linux environment] -> Keep this as an opt-in test fixture with no ports, host namespaces, host data mounts, or production reuse.
- [A healthcheck can pass before a later manager failure] -> The smoke check also queries systemd after Compose reports healthy and checks that PID 1 remains running.
- [Compose stop can return after force-killing a stuck container] -> Check exit state and reject forced-kill codes; use an explicit grace deadline and always remove the test container.

## Migration Plan

Add the Compose file and smoke gate without replacing the existing Debian CI job. Rollback removes the Compose fixture and its CI step; the current real-systemd matrix remains available throughout.
