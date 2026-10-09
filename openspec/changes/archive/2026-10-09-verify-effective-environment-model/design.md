## Context

See `proposal.md` for motivation. The existing `tests/Serval.Systemd.Tests/Fixtures/run-enumeration-tests.sh` harness owns collision checking, disposable unit setup, `daemon-reload`, service startup needed by fixtures, cleanup, and execution across the supported systemd 249/255/257/259 matrix. `RealSystemdEnvironmentSourceReaderTests` already exercises the application reader, but obtains the manager oracle by reading `/proc/<pid>/environ` in the test process. Parser and lower-level source tests cover additional semantics independently.

All generated environment values are secrets. Test infrastructure may mutate only its collision-checked disposable fixtures. Product code remains a read-only library and neither `Serval.Agent` nor `Serval.Web` participates.

## Goals / Non-Goals

**Goals:**

- Establish two independent, value-safe observations: the process launched by systemd matches the private expectation manifest, and the Serval application reader matches that same manifest for the modeled fixture variables.
- Cover every M2 construction that can be represented on the supported real-systemd baselines and explicitly map non-representable contract failures to focused managed tests.
- Make provenance, product non-mutation, cleanup, platform execution, and skipped-test handling independently auditable.
- Keep setup, privileged mutation, lifecycle, and reload authority inside the existing harness.

**Non-Goals:**

- Change the M2 environment model, parser grammar, source ordering, limits, failure contract, or application API.
- Compare the complete process environment or add `/proc/PID/environ` to the product model.
- Add production endpoints, IPC, authorization, persistence, audit storage, reload, restart, or Agent capabilities.
- Add a second container harness, package installation flow, UI tests, benchmarks, or unrelated fixes discovered while running the matrix.

## Decisions

### 1. Use a dedicated minimal test executable as the process oracle

Add a small executable under `tests/` and make it available to `Serval.Systemd.Tests` in ordinary builds and self-contained publishes. It has no product reference and performs one operation: load a private expectation manifest, compare the declared fixture-owned variable universe with its in-memory process environment, and exit with a fixed status. It accepts no expectation values or paths through command-line arguments, writes nothing to stdout or stderr, catches ordinary failures, and never includes names or values in an exception or diagnostic.

The manifest declares the complete controlled variable universe, including expected-present values and expected-absent names. The helper queries only that universe; unrelated systemd, runtime, credential, PAM, or process variables are outside the comparison.

Alternatives considered:

- Continue reading `/proc/<pid>/environ`: rejected because it bypasses the required in-process oracle and encourages comparison with an environment broader than M2.
- Use a shell, Python, or inline command: rejected because availability and parsing differ across the matrix and shell diagnostics can expose values.
- Add a helper mode to the xUnit v3 executable: rejected because it couples the fixture protocol to the test runner entry point and complicates self-contained publication.

### 2. Deliver expectations as a systemd credential

The harness generates a bounded, length-prefixed binary manifest in a fixture-private directory with mode `0700` and source file mode `0600`. The disposable unit uses `LoadCredential=` to make it available to the helper as a fixed credential name. The helper resolves only that fixed name below the systemd-provided credentials directory; no secret path or value is passed as an argument. The format is strict, versioned, bounded, rejects duplicates or trailing data, and supports UTF-8 values including empty and multiline content without delimiter ambiguity.

The C# integration test reads the private source manifest through a test-only parser and compares the Serval result in memory. Assertions use fixed labels and never interpolate names, paths, lengths, or values. Buffers containing manifest or observed values are cleared promptly.

`LoadCredential=` is available on the oldest supported systemd baseline. Credential-added process variables remain outside the declared fixture universe and therefore outside M2.

Alternatives considered:

- Environment variables or process arguments: rejected because they expose the oracle through the very channel under test or through process metadata.
- Stdout, a status file containing details, or journal messages: rejected because CI artifacts and diagnostics could retain values.
- A general shared JSON fixture: rejected because escaping, serializers, and failure output create unnecessary exposure paths.

### 3. Triangulate values and assert provenance separately

For each supported fixture:

1. the harness creates sources and the private manifest;
2. the helper runs as the disposable service and systemd records only success or a fixed failure status;
3. the test confirms the safe helper outcome;
4. `ISystemServiceEnvironmentReader` reads the stable service configuration;
5. the test compares only the manifest's controlled variable universe with the Serval values; and
6. independent assertions verify ordered source metadata and winning source IDs.

This avoids treating process-value equality as proof of provenance. Source ID `0` remains the aggregate manager contribution; each active `EnvironmentFile=` occurrence retains its own request-local ID. Tests do not claim unit-line location or reset/override history.

### 4. Extend the existing fixture family instead of adding orchestration

The existing unique prefix, manager-wide collision checks, root-owned runtime fixture location, trap-based cleanup, Ubuntu host execution, and Debian container execution remain authoritative. The harness gains fixtures for isolated `Environment=`, one file, multiple files, optional present/absent, cross-category conflict, both resets, base/template/instance drop-in selection, empty/quoted/multiline values, alias/concrete-instance reads, unsafe sources, and representable unsupported configurations.

Unsupported-property and malformed typed-reply cases cannot be produced by a conforming supported manager. They remain managed transport tests and are identified as such in traceability rather than simulated with a second D-Bus service. Other unsupported constructions are exercised against real systemd where the manager can represent them: active `UnsetEnvironment`, active `PassEnvironment`, transient/generated units, path patterns, and unresolved specifiers.

### 5. Bound a product-observation window distinct from harness activity

Harness setup, fixture helper execution, and cleanup happen outside the product-observation window. Immediately before the Serval read, tests capture fixture source/configuration identity and content, target lifecycle properties, and relevant journal state. A filtered test-only system-bus observer records only systemd manager reload signals during the read. Immediately afterward, the test verifies:

- source and configuration objects are unchanged;
- the target was not started, stopped, or restarted;
- no reload signal occurred; and
- no unexpected journal activity occurred for inactive read targets.

The observer never requests environment properties or records arbitrary bus traffic. Failure messages are fixed and value-free. Harness-owned start/stop/reload operations remain outside this window and are reported separately.

### 6. Make the traceability table the durable evidence index

Extend `docs/systemd-environment-strategy.md` with a compact table mapping each M2 requirement/construction to focused managed tests, full-flow real-systemd tests, and expected platforms. The table distinguishes `passed`, `failed`, `skipped`, and `unverified`, records why a case is not representable on real systemd when applicable, and links to test locations rather than copying requirements into task artifacts.

Local Windows managed tests, WSL real-systemd evidence, and GitHub Actions matrix evidence are reported separately. A skip is never a pass, local success never substitutes for CI, and a rerun does not erase an earlier unresolved failure.

## Risks / Trade-offs

- [The helper or manifest parser accidentally emits secret material] -> Keep both deliberately output-free, use fixed exit codes and assertion labels, clear owned buffers, and add negative tests that scan captured output and serialization for generated markers without printing them.
- [Credential behavior differs across supported systemd versions] -> Keep usage to the baseline `LoadCredential=` file form and prove it on every existing matrix entry without raising product timeouts or minimum versions.
- [A new test executable complicates publish/restore] -> Reference it only from test infrastructure, include both existing Linux RIDs and locked restore inputs, and verify ordinary build plus Debian self-contained publish on both architectures.
- [The mutation observer records harness actions as product actions] -> Establish explicit setup, helper, observation, and cleanup phases; start the filtered reload observer only for the Serval read.
- [Real-systemd races become flaky] -> Use deterministic stage-controlled mutation for inconsistency cases, fixed readiness checks, and existing operation deadlines; do not add sleeps or increase product timeouts to mask failures.
- [Some contract failures cannot be generated by a real manager] -> Preserve focused typed-transport tests and mark the real-systemd cell as intentionally not representable rather than fabricating compatibility evidence.
- [Prior matrix failures are unrelated to #41] -> Diagnose each failure first; fix only M2 verification defects within scope and report unrelated runner or infrastructure failures as failed/unverified.

## Migration Plan

No product migration is required. Introduce the test helper and manifest protocol, extend the existing fixtures and tests, then update CI wiring only as needed to include the helper in the same jobs. The previous `/proc` oracle may remain temporarily while the new path is validated, but completion removes it from full-model verification. Rollback consists of reverting the test-only additions and documentation; production binaries and runtime behavior are unaffected.
