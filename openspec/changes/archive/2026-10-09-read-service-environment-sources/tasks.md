## 1. Internal Source Contract

- [x] 1.1 Add the closed internal source-read result and disposable snapshot model for canonical identity, decoded manager source 0, ordered file occurrences 1..N, optional/missing state, and value-safe failure metadata; verify constructor, ordering, ownership-transfer, redaction, serialization, and idempotent-disposal tests pass.
- [x] 1.2 Add clearable ownership for raw file bytes and candidate-wide cleanup so failure, replacement, cancellation, and disposal clear unpublished buffers; verify focused tests observe clearing without placing generated values in assertion messages or snapshots.
- [x] 1.3 Complete the shared M2 acquisition constants for source count, per-source/total bytes, manager entries, path/configuration-path counts, and five-second deadline; verify exact-boundary and boundary-plus-one unit tests cover repeated occurrences and multibyte paths.

## 2. Typed systemd Property Boundary

- [x] 2.1 Extend the checked-in D-Bus XML, fixed proxies, and protocol seam with only the required Unit and Service property signatures, including ordered `EnvironmentFiles` tuples; verify proxy tests reject wrong signatures and prove there is no caller-selected property or `GetAll` API.
- [x] 2.2 Extend the opaque-reference transport with bounded immutable environment property snapshots and sanitized error mapping; verify tests cover missing, null, oversized, malformed, wrongly typed, and instance-mismatched replies without leaking raw payloads or remote error text.
- [x] 2.3 Refactor/reuse the M1 concrete identity resolver for inspection and source acquisition, preserving existing inspection behavior; verify direct names, ordinary aliases, instances, absence, disappearance, malformed identities, templates, protected aliases in both directions, Serval privileged families, and similarly named negative controls.

## 3. Safe Linux Source Access

- [x] 3.1 Implement bounded absolute path validation and generated/runtime classification with component-boundary rules; verify tests cover NUL/control characters, empty components, `.`/`..`, glob syntax, unresolved percent, exact generator roots and descendants, similarly named siblings, `/run/systemd/system`, and the 4,096-byte boundary.
- [x] 3.2 Implement descriptor-relative Linux no-follow resolution for content and metadata-only paths using equivalent safe `openat2`/fail-closed semantics, nonblocking final opens, type/filesystem checks, and retained object identity; verify Linux tests reject intermediate/final symlinks, magic links, FIFO, socket, devices, `/proc` pseudo-files, and traversal while accepting regular files and `/run` tmpfs sources.
- [x] 3.3 Implement bounded cancellation-aware streaming from validated descriptors into clearable ownership and cancellation checks immediately before and after synchronous Linux filesystem calls; verify tests distinguish optional initial `ENOENT`, required absence, permission/I/O failures, per-source and total-byte overruns, repeated-path accounting, timeout, cancellation, late-result disposal, and no writes to any source.
- [x] 3.4 Implement descriptor metadata and path-binding revalidation for fragment, drop-ins, present files, and optional absence; verify deterministic race tests cover path replacement, content mutation during read, disappearance, metadata change, and stable success without a check-then-reopen gap.

## 4. Source Snapshot Orchestration

- [x] 4.1 Implement first-pass validation of dirty, transient, generated, unsupported-property, `UnsetEnvironment`, `PassEnvironment`, pattern, and unresolved-specifier cases before file-content access; verify spy-based tests prove every rejection occurs before an open and empty post-reset lists remain supported.
- [x] 4.2 Implement manager-source decoding and ordered file occurrence acquisition with distinct IDs, repetitions, optional missing metadata, and complete-or-failure ownership; verify tests cover empty success, decoded manager failures, stable ordinary sources outside `/etc/serval`, ordering, and no cross-source composition.
- [x] 4.3 Implement the single five-second managed optimistic observation flow with exact property reread, canonical identity/configuration/object/binding comparison, final cancellation check, and no retry loop; verify tests map detectable changes and service disappearance to `InconsistentSnapshot`, caller cancellation to its token, observed deadline expiry to `Timeout`, and transport failures to value-safe results without claiming that a token interrupts an in-progress synchronous kernel syscall.
- [x] 4.4 Add end-to-end internal reader tests for permitted, denied, malformed, malicious, protected, and partial-read paths; verify every failure exposes only code/reason/source ID and no path, source bytes, raw D-Bus/filesystem text, partial result, or retained secret candidate.

## 5. Real Linux and systemd Verification

- [x] 5.1 Extend the disposable fixture harness with manager-wide collision checks, unique concrete units/instances/aliases, generated synthetic values, safe cleanup, and no shadowing of installed Serval privileged units; verify the harness leaves non-test unit and environment files byte-for-byte unchanged and emits no values.
- [x] 5.2 Add real-systemd cases for base fragment and lexical drop-ins, reset behavior, concrete instance/specifier handling, manager-reported file order and repetition, runtime sources, optional and required absence, protected alias/Serval-family denial, service disappearance, and an explicit unsupported construction; verify the existing 249/255/257/259 x64/ARM64 CI matrix invokes them.
- [x] 5.3 Add Linux filesystem integration cases for regular files, access denial, intermediate/final symlinks, path substitution, FIFO, socket, device, process pseudo-file, byte limits, cooperative cancellation/timeout, and mutation during read; verify synchronous filesystem calls are guarded by cancellation checks, asynchronous reads stop when cancellation is observed, and no case modifies a source.

## 6. Documentation and Quality Gates

- [x] 6.1 Update `docs/systemd-environment-strategy.md` with implementation-specific M2.5 evidence/limitations and update `docs/code-map.md` only if the new source-reader or safe-file boundary becomes a significant navigation entry; verify documentation does not claim atomicity, authorization, parsing, composition, IPC, reload, or lifecycle behavior.
- [x] 6.2 Run locked restore, formatting verification, Release build, and all applicable unit tests; record each gate as passed or failed and verify warnings remain errors.
- [x] 6.3 Run the applicable real Linux/systemd integration suite separately from Windows-skipped tests and record local versus supported-matrix evidence without treating one environment as proof of another.
- [x] 6.4 Inspect the complete issue diff against every #38 acceptance criterion and negative path, then complete `.agents/workflows/post-change-review.md`; address findings and obtain a fresh independent `VERDICT: APPROVED` after the final change with all mandatory gates passing.
