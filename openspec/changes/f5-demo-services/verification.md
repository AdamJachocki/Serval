# Verification evidence

Environment: Windows development host, Docker Desktop Linux engine, linux-x64.
Recorded on 2026-10-22. Operational instructions remain in CONTRIBUTING.md.

| Gate | Result | Evidence |
| --- | --- | --- |
| OpenSpec validation | Passed | `openspec validate f5-demo-services --strict` |
| Whitespace | Passed | `git diff --check` |
| Formatting | Passed | Full solution format verification; focused FixtureClient verification after its final diagnostic changes |
| Release build | Passed | `dotnet build Serval.slnx --configuration Release --no-restore`, zero warnings/errors; final FixtureClient also published by the smoke runner |
| Windows tests | Passed | `dotnet test --solution Serval.slnx --configuration Release --no-build --no-restore`: 485 passed, 48 Linux-dependent skipped, 0 failed |
| Linux development smoke | Passed | `pwsh -NoProfile -File eng/visual-studio-compose/test-development-host.ps1` |
| Publish separation | Passed | Staged Agent/Web publish outputs contain no demo units or development launch scripts; context preparation checks its explicit allowlist and credential-like filenames |
| Failure cleanup | Passed | `-InjectFailureAfterStart` produced its expected error; no `serval-f5-smoke-*` containers remained |
| Initial real Visual Studio acceptance | Passed, user reported | User confirmed F5/breakpoint/Shift+F5 after console-write fix; earlier container checks established unprivileged Web and debugger |
| Visual Studio acceptance with samples | Passed, user reported 2026-10-23 | User confirmed the three examples, breakpoint and Shift+F5; direct container queries confirmed matching states and full runtime verification passed. The administrator inventory is accepted unchanged. |
| Independent review | Passed | Fresh independent cleanup review returned APPROVED on 2026-10-23 with no findings. |
| Remote CI matrix | Unverified | No commit/push or remote workflow run requested; development smoke added as a separate Linux CI job |

The Linux smoke exercises a real systemd manager, exact active/inactive/failed
sample states, a locked unprivileged workload without TCP listeners, exactly one
root Agent and unprivileged capability-free Web, socket permissions, login and
CSS. A test-only debugger stand-in checks bootstrap identity and console write-only
access; it does not replace real Visual Studio debugger acceptance.

Authorization probes use the real Agent socket as the Web UID and rendered HTTP
pages: development administrator access, a generated-password non-administrator
without grants, anonymous access, malformed and malicious identifiers, and a
protected canonical unit plus its alias. Credentials and sessions stay in memory
or stdin and are not printed.

Negative runtime probes cover wrong/missing sample state, stopped Agent, existing
canonical and manager-visible alias collisions, symlink destinations, substituted
runtime directory, missing Agent executable, unsafe console ownership, unsupported
debugger arguments and unavailable manager communication. Health checks do not
repair sample state. Stop is idempotent and leaves Agent alive; systemd shutdown
must exit 0 or 130 without OOM or forced kill.

Cleanup follow-up (2026-10-23): removed the unused ignored diagnostic directories
f5-debugger-inspection and f5-probe. All retained scripts have active runtime or
test references. PowerShell syntax and isolated execution of the actual finally
block passed: no image removal before a successful build, original early failure
preserved, successful-build image cleanup retained. git diff --check passed.
