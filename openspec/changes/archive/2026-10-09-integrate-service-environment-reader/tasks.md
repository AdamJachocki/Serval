## 1. Shared operation and acquisition lifetime

- [x] 1.1 Introduce the internal operation context with monotonic elapsed-time checks, original caller token, linked cancellation and remaining transport allowance; verify controlled-clock tests for just-before deadline, exact expiry, delayed timer delivery and caller-cancellation precedence without changing M1 transport behavior.
- [x] 1.2 Refactor acquisition into a disposable session retaining transport, initial properties, configuration and file observations through final validation while transferring raw buffers separately; verify existing acquisition tests plus retained-handle validation after source-buffer disposal and cleanup on failed acquisition.
- [x] 1.3 Route all production construction paths and any acquisition compatibility wrapper through the same identifier/resolution/protection pipeline; verify malformed IDs, bare templates, missing services, canonical/alias protected services, privileged instances and similarly named permitted controls before sensitive access.

## 2. Application-facing orchestration

- [x] 2.1 Implement the existing application interface in `Serval.Systemd`, acquire sources and parse present occurrences with decreasing assignment allowance; verify optional missing slots, repeated occurrences, exact aggregate assignment limit and first excess with overridden names.
- [x] 2.2 Compose a provisional result, validate the retained observations after composition, close acquisition resources and recheck cancellation/deadline before transfer; verify manager/configuration/source mutation, path replacement, optional-file appearance and disappearance after acquisition all prevent publication.
- [x] 2.3 Preserve component failure codes, reasons and source IDs and normalize linked cancellation to the original caller token; verify parser error, required absence, unsupported configuration, transport failure and errors racing cancellation/deadline remain distinguishable and sanitized.
- [x] 2.4 Implement unconditional disposal for pre-composition candidates and post-composition unpublished results; verify instrumented buffer clearing and handle release after parser, composer, final-validation, cleanup and cancellation failures, with no secret content in test diagnostics.

## 3. Consumer and regression coverage

- [x] 3.1 Add a consumer test whose read/assertion code uses only `ISystemServiceEnvironmentReader` and application result types (adapter setup stays outside the consumer); verify full masked metadata/provenance, empty declarations, alias equivalence, metadata usability after secret disposal, and existing serialization redaction without public reveal access.
- [x] 3.2 Add deterministic stage-controlled cancellation/deadline tests covering resolution, connection/acquisition, parsing, composition, final validation and pre-publication cleanup; verify one total budget, late synchronous-call cleanup and original caller-token precedence, without sleep-based timing assertions.
- [x] 3.3 Add concurrent reads of two services with overlapping names plus repeated reads after a source change; verify no mixed metadata/values, no cached result and independent disposal, and rerun existing inventory/identity, source-reader, parser and composer regression suites.
- [x] 3.4 Extend the existing collision-checked real-systemd harness to call the application reader for full precedence/provenance, repeated files, resets, optional absence, required absence, empty declarations, alias/instance identity, protected targets and unsupported configuration; verify generated values remain private, non-test sources are unchanged, fixtures are cleaned and the supported 249/255/257/259 x64/ARM64 CI jobs exercise the integrated path.

## 4. Documentation and quality gates

- [x] 4.1 Update the canonical environment strategy and code map with adapter/session ownership, provisional publication, the shared managed deadline limitation and future Agent authorization/consumer disposal obligations; verify documentation matches the implementation and no Web/Agent registration, inventory change, endpoint, cache or persistence was introduced.
- [x] 4.2 Run locked restore, formatting verification, Release build with warnings as errors, applicable managed and real-systemd tests and strict OpenSpec validation; record each result and distinguish local Linux evidence from the supported CI matrix, including any existing configuration warning.
- [x] 4.3 Perform the task-wide acceptance self-check and mandatory fresh independent review under `.agents/workflows/post-change-review.md`; address blocking findings, repeat affected checks and obtain `VERDICT: APPROVED` before considering implementation complete.

## Validation evidence (2026-09-24)

- Passed: `dotnet restore Serval.slnx --locked-mode`.
- Passed: `dotnet format Serval.slnx --verify-no-changes --no-restore` after applying the formatter's whitespace-only corrections.
- Passed: `dotnet build Serval.slnx --configuration Release --no-restore` with zero warnings and zero errors.
- Passed: `dotnet test --solution Serval.slnx --configuration Release --no-build --no-restore`; 410 passed and 28 Windows-inapplicable real-Linux/systemd cases skipped.
- Passed local Linux evidence: the collision-checked disposable harness ran as root in Ubuntu WSL x64 with systemd 255; all 364 `Serval.Systemd.Tests` cases passed with zero failures and zero skips, and the harness cleaned its fixtures.
- Passed: `openspec validate integrate-service-environment-reader --type change --strict --json --no-interactive`.
- Unverified locally: the supported GitHub Actions systemd 249/255/257/259 x64/ARM64 matrix. The existing workflow invokes the same integrated harness for those jobs; the local systemd 255 pass does not substitute for CI.
- Existing warning observed while reading apply instructions: `openspec/config.yaml` could not be parsed at line 63. Strict validation of this change still passed; repairing that pre-existing configuration is outside this change.
- Passed: fresh independent `serval-code-reviewer` review returned `VERDICT: APPROVED` with no blocking findings.
