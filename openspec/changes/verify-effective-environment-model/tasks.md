## 1. Establish the test-only oracle

- [x] 1.1 Add the minimal environment-comparison executable under `tests/` with no product reference, command-line inputs, or intentional standard output/error; verify focused tests cover fixed success, mismatch, malformed-manifest, missing-credential, duplicate-name, trailing-data, and over-limit exit statuses without exposing names, paths, lengths, or values.
- [x] 1.2 Define the bounded versioned length-prefixed expectation manifest and shared test-only parser/owner, including expected-present and expected-absent entries; verify empty, Unicode, quoted, multiline, duplicate, malformed, exact-limit, first-excess, disposal, and buffer-clearing cases use only fixed assertion labels.
- [x] 1.3 Wire the helper into `Serval.Systemd.Tests`, the solution, locked restore, ordinary build, and existing `linux-x64`/`linux-arm64` self-contained publish outputs; verify both RID publishes contain a runnable helper without adding a runtime dependency or production project reference.

## 2. Extend the existing real-systemd fixture safely

- [x] 2.1 Extend `run-enumeration-tests.sh` within its existing unique-prefix and manager-wide collision checks to create a private `0700` fixture directory, `0600` manifest sources, fixed-name `LoadCredential=` declarations, and helper-backed disposable units; verify no expectation value or secret path is placed in arguments, stdout, stderr, journal output, or test reports.
- [x] 2.2 Add fixture cases for isolated `Environment=`, one file, ordered multiple and repeated files, optional present and absent files, manager/file conflicts, `Environment=` and `EnvironmentFile=` resets, base/template/instance drop-in selection, empty values, quoting, continuation, multiline content, aliases, and concrete instances; verify setup performs a manager-wide collision check for every concrete name and uses only generated synthetic values.
- [x] 2.3 Extend trap-based cleanup for every new unit, alias, drop-in, credential source, environment file, unsafe-source object, monitor, and helper process; verify a deliberately failing test run still removes the fixtures and completes the final harness-owned `daemon-reload` without touching non-test sources.

## 3. Verify values and provenance through the application contract

- [x] 3.1 Replace the full-model `/proc/<pid>/environ` oracle with helper-result and private-manifest comparisons through `ISystemServiceEnvironmentReader`; verify exact presence and value only for the complete fixture-owned variable universe while ignoring unrelated process environment.
- [x] 3.2 Add full-flow theories for every supported fixture construction from task 2.2 and verify canonical identity, exact controlled values, optional/missing flags, aggregate manager source ID `0`, per-occurrence file source IDs, winning provenance, and absence of reset variables with value-free diagnostics.
- [x] 3.3 Keep parser-specific baseline-divergence coverage separate from the full-model oracle and remove only redundant full-model `/proc` reading; verify existing real-systemd parser, loaded-environment, source-reader, composition, inventory, and D-Bus suites continue to pass.

## 4. Verify failures, consistency, and non-mutation

- [x] 4.1 Add real-systemd fixtures for representable unsupported configurations—active `UnsetEnvironment`, active `PassEnvironment`, transient unit, generated configuration, path pattern, and unresolved specifier—and unsafe/wrong sources including required absence, symlink, and special file; verify each fails with its exact value-free code/reason before forbidden content access and never returns partial values.
- [x] 4.2 Retain typed-transport coverage for `UnsupportedProperty` and malformed manager replies that a conforming supported manager cannot produce; verify the traceability evidence marks these cases as intentionally not representable rather than passed or skipped real-systemd tests.
- [x] 4.3 Exercise protected canonical targets, protected aliases, Serval privileged-family instances, similarly named permitted controls, and malformed/nonexistent identifiers through the application contract; verify denial precedes value-bearing property and file access.
- [x] 4.4 Add deterministic real-systemd-backed full-reader races for source/configuration mutation, optional-file appearance/disappearance, and manager/service disappearance; verify `InconsistentSnapshot`, complete cleanup, no retry loop, and no partial publication without sleep-based timing or increased product deadlines.
- [x] 4.5 Establish an explicit product-observation window after fixture/helper setup and before cleanup, snapshot source/configuration bytes and identity plus target lifecycle/journal state, and observe only manager reload signals; verify a Serval read performs no write, start, stop, restart, or `daemon-reload`, while harness-owned operations remain outside that observation.
- [x] 4.6 Capture helper output, relevant journal data, serialized results, and test-report inputs in negative paths and scan them in memory for generated markers; verify leakage fails with a fixed message that does not reproduce the marker or surrounding data.

## 5. Platform evidence and durable traceability

- [x] 5.1 Diagnose the prior Debian 13 x64/ARM64 and Ubuntu 24.04 ARM64 real-systemd job failures before changing orchestration; verify each is classified as an M2 test defect, harness defect, runner/infrastructure failure, or unverified condition, and fix only in-scope verification defects without removing runners, raising minimum systemd versions, or increasing product timeouts.
- [x] 5.2 Update only the existing Ubuntu 22.04/24.04/26.04 and Debian 13 x64/ARM64 jobs as needed to publish and execute the helper through `run-enumeration-tests.sh`; verify a disabled/skipped real-systemd suite makes the job fail or remain explicitly unverified rather than reporting compatibility success.
- [x] 5.3 Extend `docs/systemd-environment-strategy.md` with the M2 requirement/construction → focused test → full-flow test → platform traceability table and intentional model limitations; verify it distinguishes passed, failed, skipped, unverified, and intentionally non-representable evidence without copying secrets or claiming local evidence for CI.
- [ ] 5.4 Run and record Windows managed results, any authorized WSL real-systemd run, and every supported CI matrix cell separately; verify fixture cleanup after success and failure, retain unresolved failed/unverified status, and do not rerun unchanged failures until green.

## 6. Completion gates

- [x] 6.1 Run locked restore, formatting verification, Release build with warnings as errors, the complete managed suite, applicable helper publish tests, real-systemd harness, and strict OpenSpec validation; record every gate as passed, failed, skipped, or unverified with a reason and confirm reports contain no generated values.
- [ ] 6.2 Perform the issue #41 acceptance self-check against the proposal/design and the repository's post-change review workflow; address blocking findings, repeat affected gates, and obtain a fresh independent `VERDICT: APPROVED` before marking the change complete.
