## 1. Compose test host

- [x] 1.1 Add a Compose service that builds the existing Debian 13 Dockerfile, runs `/sbin/init` as PID 1, uses container-owned runtime tmpfs, and has no volumes or published ports; verify `docker compose config` resolves to the intended image, command, privileges and mount set.
- [x] 1.2 Add a bounded healthcheck that checks PID 1 and queries the live systemd manager; verify a healthy result after startup and an unhealthy result when the manager query is unavailable.

## 2. Lifecycle smoke test

- [x] 2.1 Add a repeatable smoke runner for bounded Compose startup, an explicit `systemctl` query, running-state check, graceful stop and exit-state assertion; verify it passes on a supported Linux Docker Engine and reports a nonzero result for startup/query/forced-stop failures.
- [x] 2.2 Make the runner inspect the resolved Compose service and created container mounts, reject any bind or named volume and always remove the container on success or failure; verify the negative mount check and cleanup path.

## 3. CI and local use

- [x] 3.1 Add a focused Linux CI smoke gate invoking the Compose runner, preserving the current Debian x64/ARM64 real-systemd contract matrix; verify the new gate passes and existing matrix configuration remains intact.
- [x] 3.2 Document Docker Desktop with Linux containers: build/start, manager query, stop, expected result, cleanup and requirements in the relevant developer guide; verify the documented commands complete on Docker Desktop without mounting host accounts, PAM, repository data, the Docker socket or other host data.
- [x] 3.3 Complete the applicable repository checks and independent post-change review; verify all mandatory gates pass and the latest reviewer returns `VERDICT: APPROVED`.
