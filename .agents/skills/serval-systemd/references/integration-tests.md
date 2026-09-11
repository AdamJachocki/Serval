# Real-systemd integration matrix

Read this reference before completing a systemd implementation or review. Unit tests may supplement these cases but cannot replace a real Linux/systemd test environment.

Use disposable test units and temporary Serval-owned fixtures. Assert both the operation result and that no vendor/user-owned source changed. Never print real or generated environment values in CI output.

Required coverage:

| Area | Minimum real-systemd cases |
| --- | --- |
| Source reading | `Environment=`; one `EnvironmentFile=`; multiple ordered `EnvironmentFile=` declarations; optional/missing file where supported |
| Effective values | Conflicting assignments demonstrating systemd precedence; provenance remains correct and values remain masked |
| Serval override | Add/change a Serval-only override; inherited source remains byte-for-byte unchanged |
| Removal | Remove a Serval override and observe the current inherited value becoming effective; clean up empty Serval artifacts |
| Reload | Successful `daemon-reload`; reload failure reported without restart or partial-file corruption |
| Lifecycle | Explicit permitted restart; save without restart; restart denied independently of edit permission |
| Validation | Malformed and malicious unit names; traversal/path manipulation; non-system or nonexistent target |
| Authorization | Missing privileges; unauthorized service; protected service; Serval's own privileged components |
| Failure safety | Interrupted/failed write leaves the previous complete file; symlink target is rejected |

For every privileged operation, include both permitted and denied paths and apply `serval-privileged-operation` as well.

## Local Windows/WSL execution

Before preparing a local run, verify that the selected WSL distribution runs
systemd as PID 1 and provides the required system bus. Inspect the existing
`tests/Serval.Systemd.Tests/Fixtures/run-enumeration-tests.sh` harness and the CI
commands; reuse its disposable fixtures and cleanup rather than recreating units
manually. Running the harness needs the execution permissions appropriate to its
systemd mutations; this guidance does not itself grant elevation.

When publishing on Windows, select the Linux runtime matching the WSL architecture
and publish self-contained test output into a task-specific ignored artifact
directory. Copy the complete output to a fresh `mktemp -d` directory on the Linux
filesystem, such as under `/tmp`, and execute the tests there. Prefer this over
running binaries directly from `/mnt/c` or `/mnt/d`: cross-filesystem loading can
consume short operation deadlines. Keep source and repository edits in the
original workspace. Remove only the temporary directory created for this run,
after verifying its resolved path belongs to the intended temporary root.

If an operation times out, record the failing test and deadline, then investigate
startup, filesystem placement, system-manager readiness, or host load before
retrying. Do not raise production deadlines or disable assertions to accommodate
a slow development host. A successful rerun is evidence for that environment,
not proof of the exact cause of an earlier failure. Keep reporting any unresolved
failure instead of rerunning until an intermittent test happens to pass.

Report local Linux/systemd results separately from Windows tests (which skip the
real-systemd cases) and the supported-distribution/architecture CI matrix. A local
WSL pass does not establish that the CI matrix passed or replace its requirement.
