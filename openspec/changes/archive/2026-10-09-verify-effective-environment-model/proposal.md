## Why

Serval's integrated environment reader is covered by real-systemd tests, but the current end-to-end oracle reads `/proc/<pid>/environ`, does not exercise every accepted M2 construction, and has not produced a fully green supported-platform matrix. Issue #41 closes those verification gaps without changing the product environment model or adding privileged product capabilities.

## What Changes

- Extend the existing collision-checked real-systemd harness with a minimal test-only process that compares only fixture-owned environment variables in memory and reports a fixed, value-free outcome.
- Deliver generated expectations through a private permission-restricted fixture channel, never through process arguments, standard streams, the journal, snapshots, or published test data.
- Verify the complete application-reader flow for the remaining supported source, precedence, reset, quoting, multiline, alias, instance, drop-in, failure, protection, and consistency cases required by M2.
- Verify source provenance separately from process-value agreement, preserving the M2.1 aggregate-manager and concrete-file-occurrence model.
- Separate harness setup and cleanup operations from observations proving that a Serval read does not write sources or invoke service lifecycle actions or `daemon-reload`.
- Run the expanded cases on the existing Ubuntu and Debian x64/ARM64 matrix, report local, WSL, and CI evidence separately, and never treat a skipped real-systemd run as compatibility evidence.
- Add durable M2 requirement-to-test-to-platform traceability and document intentional model limitations in the canonical environment strategy.

## Capabilities

### New Capabilities

None. This change strengthens verification of the existing environment-reading model.

### Modified Capabilities

None. No product requirement or externally observable behavior changes; the change opts out of delta specs.

## Impact

- Affected test infrastructure: `tests/Serval.Systemd.Tests`, its existing `Fixtures` harness, and a minimal test-only executable if required for the in-process comparison oracle.
- Affected CI: the existing real-systemd jobs in `.github/workflows/ci.yml`; no second container or manual administration workflow is introduced.
- Affected documentation: `docs/systemd-environment-strategy.md` gains the verification traceability table and explicit limitations.
- Security impact: generated values remain secret from creation through cleanup; new privileges are confined to the existing test harness and are not added to `Serval.Agent`, `Serval.Web`, IPC, or production code.
- Dependencies: no new runtime dependency or production project reference is intended.
