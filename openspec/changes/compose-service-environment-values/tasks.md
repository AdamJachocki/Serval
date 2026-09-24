## 1. Complete Input Contract

- [x] 1.1 Add an internal composition entry point that accepts the M2.5 successful source snapshot and one ordered parsed-candidate slot per file occurrence, with explicit ownership transfer; verify contract tests cover an empty manager-only snapshot, present and optional-missing occurrences, and disposal after success.
- [x] 1.2 Validate slot count/order, consecutive IDs, optional/missing state, distinct candidates, source-byte correspondence, and each candidate's name/value/source-ID consistency before publication; verify negative tests reject omitted, extra, swapped, duplicate, and malformed candidates without exposing values or leaving owned buffers live.

## 2. Deterministic Composition

- [x] 2.1 Apply decoded manager winners followed by parsed file winners in manager occurrence order using ordinal case-sensitive names, and publish final metadata in ordinal-name order; verify table-driven tests cover manager-only, within-source duplicates, manager/file conflict, two files, repeated file occurrence, case-distinct names, empty value, and optional missing source.
- [x] 2.2 Populate source metadata from the acquired occurrence list and winning-source IDs only, with no path or unit-line history; verify tests inspect exact source order and provenance for manager, files, repeated paths, and reset-to-final-manager inputs.
- [x] 2.3 Enforce fixed 65-source, 4,194,304-byte, and 16,384-assignment aggregate bounds using pre-deduplication counts and repeated occurrences; verify exact-limit successes and first-excess `LimitExceeded` failures, including overridden assignments and repeated-file byte accounting.

## 3. Secret Ownership and Failure Paths

- [x] 3.1 Copy selected spans into the final disposable value owner, clear replaced output buffers promptly, and dispose all input candidates and raw snapshot buffers on every exit; verify instrumentation observes losing and unpublished buffers cleared after replacement, malformed input, limit failure, and result-construction failure.
- [x] 3.2 Honor caller cancellation before work, between bounded contributions, and before publication, while preserving its token and clearing unpublished data; verify cancellation tests for pre-canceled and mid-composition cases and assert fixed value-free diagnostics, serialization, and string output with synthetic secrets.

## 4. Real Systemd Evidence and Completion

- [x] 4.1 Extend the collision-checked disposable fixture and test-only acquisition/parse/compose path to compare generated synthetic winning values with real systemd on supported 249/255/257/259 CI baselines; verify manager/file conflict, later file, repeated declaration, empty value, optional missing source, and unit/drop-in reset without printing values or changing non-test files.
- [x] 4.2 Update `docs/systemd-environment-strategy.md` with only the M2.6 implementation evidence or limitations and `docs/code-map.md` if the composer becomes a significant navigation entry; verify both accurately retain the #40 integration and Agent authorization boundaries.
- [x] 4.3 Run locked restore, format verification, Release build, focused tests, and applicable real-systemd tests; record passed, failed, and unverified gates separately, then complete the mandatory fresh independent post-change review with `VERDICT: APPROVED` before considering issue #39 implemented.
