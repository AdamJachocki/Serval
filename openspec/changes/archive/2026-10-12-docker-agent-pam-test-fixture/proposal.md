## Why

The disposable Debian Docker host verifies systemd startup, but it cannot start the root Agent with its required Linux identity, PAM service, and protected paths. Consequently, the real deployment checks for the socket peer and PAM login have no self-contained container test.

## What Changes

- Add a separate, disposable Agent Docker fixture with the required locked `serval-web` identity, root-owned Agent configuration, PAM policy, runtime and policy directories, and published Agent executable.
- Provision test login accounts only for the lifetime of a test container. Supply the test password at execution time outside the repository, image layers, container metadata, and logs.
- Add automated checks that the Agent starts as root, creates the expected socket, accepts only the configured kernel peer UID, and handles valid credentials, invalid credentials, a PAM-disallowed account, and unsafe deployment configuration.
- Run the fixture checks in CI and document local execution and cleanup.

## Capabilities

### New Capabilities

None. This change adds test infrastructure for existing Agent behavior.

### Modified Capabilities

None. The accepted `identity/system-account-sessions` and `ipc/agent-operations` requirements remain unchanged. This change opts out of delta specs.

## Impact

- Test-only Docker and orchestration files, Agent integration tests, and a CI job or step.
- Canonical developer documentation for running the disposable fixture.
- No production Agent, Web, IPC, PAM deployment contract, or service authorization change. The container uses root only to exercise the existing privileged boundary.
