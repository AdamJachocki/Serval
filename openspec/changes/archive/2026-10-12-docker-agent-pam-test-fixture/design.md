## Context

See `proposal.md`. `AgentConfiguration` fixes `/etc/serval/agent.json`, `/etc/pam.d/serval`, `/run/serval/agent.sock`, and `/var/lib/serval/policy.db`; startup rejects unsafe ownership, modes, missing identities, and a missing PAM file. `AgentSocketServer` checks `SO_PEERCRED` UID and creates a root:`serval-web` socket with mode `0660` inside a root:`serval-web` `0750` directory. The existing Debian 13 image and Compose smoke service intentionally contain no Agent material. Accepted identity and IPC behavior remains in `openspec/specs/identity/system-account-sessions` and `openspec/specs/ipc/agent-operations`; deployment details are in `docs/identity-deployment.md`.

## Goals / Non-Goals

**Goals:**

- A separate disposable image that runs the actual published Agent as root with the production path and identity checks intact.
- End-to-end Linux evidence for socket ownership and peer authentication, PAM authentication and account rejection, and fail-closed startup.
- A test runner whose credential exists only in process memory and an anonymous stdin pipe during a container run.

**Non-Goals:**

- A production container deployment or a substitute for the supported host installation.
- Changing the Agent's protocol, PAM service contract, Web behavior, authorization rules, or protected-service policy.
- Adding real host accounts, host PAM files, host filesystem mounts, or test passwords to image layers.
- Exercising service inventory, grant management, or systemd mutations; the existing real-systemd contract matrix covers those paths.

## Decisions

### Keep the Agent fixture separate from the systemd smoke image

Build the existing Debian 13 systemd base, then build a test-only Agent image from it. Stage an allowlisted Docker build context containing only the Agent publish output, the existing `deploy/pam.d/serval` policy, the existing Agent unit, and fixture setup files. Publish with locked dependencies before staging. The Agent image contains the locked `serval-web` system account with `/nonexistent` home, `/usr/sbin/nologin` shell, its own primary group, and no supplementary groups; a dedicated existing test admin group; root-owned Agent configuration and policy directory; and the executable under `/opt/serval/agent`. No test-login password or password hash is built into the image. This avoids widening the existing smoke image or sending the repository and untracked files as Docker build context. An all-in-one image or repository-root Docker context would weaken that isolation.

Run systemd as PID 1 with container-local `/run` and `/run/lock` tmpfs. Install the existing Agent unit and start it through systemd so `RuntimeDirectory=serval` creates the expected root:`serval-web` `0750` directory. Do not use a host bind mount, host network namespace, Docker socket mount, or published port. Inspect the effective container mounts before testing. Agent runs as root and its only exposed IPC endpoint remains container-local `/run/serval/agent.sock`. No new Serval root capability is introduced.

### Create disposable login accounts after container startup

The runner creates two fixed-purpose local accounts in the disposable container: an admitted account and an account that has a valid password but fails the PAM account phase, for example through an expired account. They are never members of the Web or admin group. The accounts and their password hashes disappear with the container. The runner generates a fresh high-entropy test password in memory for each run, or accepts one through a protected, non-repository stdin source for local execution. It passes account/password records to `chpasswd` via an anonymous stdin pipe. It never places the password in Docker build arguments, environment variables, command arguments, files, artifacts, exceptions, test names, or logs. CI uses only generated credentials. Negative-password input is separately generated in memory. Commands and failure reporting use fixed labels and sanitized exit states; never echo credential-bearing stdin or PAM output.

### Exercise the real protocol and kernel peer check

Use a small test-only typed IPC client or the existing protocol library, executed in the container under `serval-web`, to issue login requests. Assert that the valid account gets an opaque session, the wrong password and disallowed account get no session and no account-detail disclosure, and a separate non-Web UID is rejected before dispatch even when it sends a syntactically valid login request. A root peer is also untrusted because the configured Web UID alone is allowed. The client must report only result categories, never submitted credentials or session contents. Inspect numeric UID/GID and modes of `/run/serval` and `agent.sock`, Agent process identity, and service health. For failure cases, stop Agent, change one fixture-controlled configuration condition at a time (for example wrong Web UID, unsafe mode, missing PAM file), verify a bounded failed start with no listening socket, then restore the original safe state. Keep every mutation inside the disposable container.

The attacker-controlled fields in this fixture are the login username and password, protocol frame, and connecting process identity; Docker/test orchestration controls setup paths and configuration. Agent derives peer UID from `SO_PEERCRED`, validates deployment state itself, and handles PAM without persisting the password. The test uses only the existing narrow login operation; no shell or arbitrary path is exposed through IPC. No service target, environment value, or privileged filesystem write is accepted from an IPC request. The fixture's root setup uses fixed paths and account names and is not a product API. Failure must remain sanitized and fail closed. Existing Agent policy and audit behavior is unchanged; this test neither grants permissions nor emits secret audit metadata.

### Make CI and local execution bounded and disposable

Add a Linux CI gate that builds the images, runs the fixture, asserts every positive and negative case, and removes containers and staged build context even on failure. Retain the existing Compose smoke and Debian/Ubuntu real-systemd matrices. Document Docker Desktop Linux-container prerequisites and a single local invocation. Capture bounded diagnostic status without secrets; avoid full container logs in the credential-bearing phase. Cleanup checks the exact test-owned container and temporary directory before removal.

## Risks / Trade-offs

- [A privileged Docker test container has broad access to its Docker VM] → Keep it opt-in/test-only, container-local, without host mounts, ports, or namespaces, and clean it up after every run.
- [Password exposure through command tracing or failure output] → Pass it only through stdin, disable verbose tracing for that operation, redact diagnostics by construction, and verify logs/artifacts do not contain the generated value without printing it.
- [A service start can briefly succeed before Agent exits] → Poll service and socket state within a fixed deadline and assert sustained active state; for negative cases require final failure and no socket.
- [Debian PAM/account behavior differs from the host] → Use the deployed PAM file and real Debian PAM/NSS; keep the existing host-systemd CI matrix as separate evidence.

## Migration Plan

Add the Agent fixture and CI gate alongside existing tests. Rollback removes only the test image, runner, and CI invocation. No production database, unit, or IPC migration is required. Document the fixture workflow in the canonical developer guide; do not promote its disposable account setup to production deployment guidance.
