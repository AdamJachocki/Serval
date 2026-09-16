## 1. Parser Contracts and Limits

- [ ] 1.1 Add the internal environment-file parse result/candidate contract with positive file source IDs, value-free failures, exact byte/assignment accounting, and disposal semantics while leaving declaration-level source metadata to the future composer; verify focused contract tests reject invalid arguments and serialization/string output contains no generated secret.
- [ ] 1.2 Centralize the applicable source, logical-record, and assignment limits without changing the existing loaded-entry behavior; verify all existing `LoadedEnvironmentDecoderTests` still pass.

## 2. Grammar and Secret Ownership

- [ ] 2.1 Implement the first-pass raw-byte state machine for strict UTF-8 validation and systemd comment, blank-line, ignored-line, separator, whitespace, quote, escape, continuation, and multiline rules; verify table-driven tests cover every state plus malformed UTF-8, NUL, U+FEFF in every position, representative Unicode noncharacters from each defined range, invalid names/assignments, and unterminated quotes.
- [ ] 2.2 Implement deterministic rejection of `#` and `;` comments ending in backslash before LF-only or CR-only endings without branching on manager version, while accepting the stable CRLF form; verify focused tests return value-free `InvalidSource` without parsing the following line for divergent inputs and parse it for CRLF.
- [ ] 2.3 Implement exact raw-source, logical-record, and remaining-assignment accounting during validation; verify equality succeeds and boundary-plus-one returns value-free `LimitExceeded` for single-byte, multibyte, continued, ignored, and overridden inputs.
- [ ] 2.4 Implement the second-pass decoding into clearable temporary buffers and `EnvironmentValues`, including ordinal metadata, empty values, duplicate replacement, and winning source provenance; verify focused tests cover exact values without emitting them in test failure messages.
- [ ] 2.5 Dispose and clear all temporary, losing, failed, canceled, and unpublished owned buffers on every exit; verify instrumented tests cover duplicate replacement and failure/cancellation after earlier secret assignments without exposing generated markers.
- [ ] 2.6 Add cancellation checks before work, throughout both passes, and immediately before publication; verify pre-canceled and mid-parse tests throw `OperationCanceledException` with the caller token for empty, valid, invalid, and secret-bearing sources.

## 3. Compatibility Verification

- [ ] 3.1 Extend the disposable real-systemd fixture with uniquely named private units and environment files covering all supported quoting modes, escapes, continuations, multiline values, LF/CR/CRLF comments, ignored lines, empty/duplicate assignments, permitted and forbidden Unicode, literal shell/specifier-looking content, and the trailing-backslash comment divergence; verify collision checks and cleanup preserve every non-test source byte-for-byte.
- [ ] 3.2 Compare common-subset parser output with the manager-observed environment on systemd 249, 255, 257, and 259 without printing values, and assert pre-254/post-254 behavior plus Serval's uniform `InvalidSource` result for LF-only/CR-only divergent fixtures and common parsing for CRLF; verify the real-systemd integration test passes in the configured CI matrix and record local Linux/systemd results separately from Windows skips.

## 4. Quality Gates

- [ ] 4.1 Run repository formatting verification, build with warnings as errors, the complete unit test suite, and relevant real-systemd integration tests; verify every mandatory gate passes and report any environment-only skips or blockers explicitly.
- [ ] 4.2 Run the mandatory fresh-context `serval-code-reviewer` workflow, address every finding, rerun affected gates, and verify the latest independent verdict is `VERDICT: APPROVED`.
