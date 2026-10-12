## Why

The Debian 13 real-systemd image is started only through a long CI-specific `docker run` command. There is no small, repeatable Compose entry point that verifies systemd startup and shutdown on both CI and Docker Desktop before heavier contract tests run.

## What Changes

- Provide one Debian 13 test image based on the existing `tests/Serval.Systemd.Tests/Fixtures/DebianSystemd.Dockerfile` and a Docker Compose service that runs systemd as PID 1.
- Add a bounded startup smoke check that verifies the container stays running, systemd reaches a queryable state, and a normal Compose stop shuts it down within a stated deadline.
- Run the same Compose smoke check in CI and document its local Docker Desktop invocation and cleanup.
- Keep the smoke service self-contained: no host Linux accounts, PAM configuration, Docker socket, repository data, or other host data bind mounts.

## Capabilities

### New Capabilities

None. This is test infrastructure, not a new Serval product capability.

### Modified Capabilities

None. Accepted product requirements stay unchanged; this change opts out of delta specs.

## Impact

- Test infrastructure: the existing Debian 13 Dockerfile, a Compose file and a startup smoke-check script under test fixtures.
- CI: add a bounded Compose startup job or step, without replacing the existing real-systemd contract matrix or its disposable-unit harness.
- Documentation: record the Docker Desktop command, prerequisites, expected readiness and cleanup in a canonical developer guide.
- Security: the Compose service may need container-local systemd privileges, but adds no Serval.Agent, Web, IPC, PAM, account or production deployment capability. It has no host data mounts.
