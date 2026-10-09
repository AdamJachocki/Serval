## Context

See [proposal.md](proposal.md) for motivation and [specs/service-environment-composition/spec.md](specs/service-environment-composition/spec.md) for the behavior contract. The accepted `docs/systemd-environment-strategy.md` and `.agents/skills/serval-systemd/references/effective-environment.md` define source precedence, bounded ownership, and the future Agent boundary. `SystemdEnvironmentSourceReadResult.Success` already owns canonical identity, decoded manager source 0, and ordered raw file occurrences; `EnvironmentFileParser` returns a disposable parsed candidate for one present file. `ServiceEnvironmentReadResult.Success` and `EnvironmentValues` provide the final metadata/value contract.

The composition code is internal to `Serval.Systemd`. It has no runtime registration. `Serval.Web` remains unprivileged and `Serval.Agent` remains inert; no caller identity, IPC, authorization, persistence, or new root capability is added.

## Goals / Non-Goals

**Goals:**

- Establish a complete-input handoff that binds one parsed contribution to each present occurrence in the already validated acquisition snapshot.
- Produce one deterministic supported-declarations result while clearing losing and unpublished values.
- Independently check operation-wide counts even if a future caller supplies malformed internal candidates.
- Leave a narrow interface for #40 to parse source bytes with a decreasing allowance and call composition within its existing read deadline.

**Non-Goals:**

- Parse file bytes inside the composer or add another systemd property, filesystem read, or source discovery path.
- Construct a process's full environment or replay unit/drop-in directives, `UnsetEnvironment`, `PassEnvironment`, or future Serval overrides.
- Implement the application reader or reveal values to a principal.

## Decisions

### Bind parsed candidates to the acquired occurrence list

Use the successful M2.5 source snapshot as the authority for canonical identity, source count, order, optionality, and missing state. The composer accepts it together with an ordered, indexed list of parsed file candidates: exactly one candidate for each present file and a null slot only for a missing optional file. Validate list length, consecutive source IDs, each candidate's source ID and name/value correspondence, optional/missing invariants, and recorded byte count against the corresponding owned raw source before publishing. Reject duplicate candidate object references and malformed candidates with fixed, value-free argument diagnostics; no partial result escapes. A caller-supplied dictionary or set cannot express authoritative order and is not accepted.

The snapshot and parsed candidates transfer ownership at invocation, including when validation, cancellation, or result construction fails. The composer releases all input owners on every exit. This avoids requiring the future #40 adapter to infer whether a partially consumed input still needs disposal. #40 must dispose inputs itself when parsing fails before it invokes the composer.

Alternative considered: accept only a list of parsed candidates. Rejected because it cannot prove that a present manager-declared occurrence was omitted or that an optional missing occurrence retained its position.

Alternative considered: parse raw snapshot bytes inside the composer. Rejected because #39's input is already parsed and #40 owns acquisition-to-parser orchestration, including the decreasing assignment allowance.

### Count before copying and apply contributions in source order

Use the existing `EnvironmentReadLimits`. Account for manager entry count plus every parsed file's pre-deduplication `Assignments`, and manager `SourceBytes` plus every present parsed file's raw `SourceBytes`; count repeated occurrences again. Check source and byte bounds, count with overflow-safe arithmetic, and return `LimitExceeded` with the first offending request-local source ID. The #40 caller will pass `MaxAssignments - consumedAssignments` to each parser in source order; this composer rechecks totals as defense against a malformed or separately constructed candidate. The immutable M2.5 snapshot already enforces source and raw-byte bounds, but a repeated check at this handoff protects the composition contract.

After validation, apply manager variable names and then each present file candidate's retained names in occurrence order. `EnvironmentValues.Set` copies a winning span and clears its previous owned buffer when a later source wins. Update a separate ordinal-name-to-winning-source map at each assignment. A zero-length span still creates a present value. Sort only the final metadata projection with `StringComparer.Ordinal`; sorting never changes precedence. Construct `EnvironmentSourceMetadata` from snapshot occurrence fields, so no path or original unit location enters the result.

Alternative considered: sort contributions or merge a single globally ordered list of raw assignments. Rejected because file contributions have precedence over the complete manager aggregate regardless of original unit-line order, and parser candidates have already collapsed duplicates within a source.

### Make publication the only ownership transfer to the consumer

Create a fresh `EnvironmentValues` for the output. Keep it and all input candidates under `try/finally` ownership while composing. On a successful `ServiceEnvironmentReadResult.Success` construction, freeze the output and transfer its disposal responsibility to the result consumer; then dispose the manager and parsed source candidates and raw snapshot buffers. On failure or cancellation, dispose output and all inputs before any exception or controlled failure escapes. Replacing an output value clears the losing output buffer immediately. Parsed input owners are released promptly after their values have been copied, with a final cleanup path for unprocessed inputs.

Use only fixed exception text without inner exceptions or raw source data. `LimitExceeded` is a controlled result; structurally invalid internal input is a sanitized argument error. Observe caller cancellation before validation, between bounded source/name loops, and before publication. A linked deadline token supplied by #40 can stop composition under the existing five-second operation budget; this change adds no independent timer or per-source deadline.

Alternative considered: reuse an input `EnvironmentValues` as the output owner. Rejected because it cannot safely transfer selected values between independently owned sources or guarantee prompt removal of losing buffers with the current M2.2 API.

### Verify systemd semantics without adding a production reader

Extend the existing disposable real-systemd fixture with generated, private manager/file conflicts, later-file wins, a repeated file occurrence, empty values, optional missing occurrence, and reset-to-final-manager behavior. A test-only path acquires the existing source snapshot, parses each present file with the decreasing allowance, and calls the composer. Compare the result with the fixture's manager-observed process environment using the established test oracle without printing values or storing them in snapshots. Assert source IDs against the manager's final ordered declaration list, not against unit-file line locations. Keep the fixture collision checks, cleanup, and supported 249/255/257/259 CI matrix.

Alternative considered: treat isolated composition tests as sufficient for precedence. Rejected because the accepted systemd strategy requires confirmation against real manager behavior on supported baselines.

### Trust boundary and permanent documentation

Inputs are internal but derive from untrusted D-Bus replies, file bytes, and filesystem observations. M2.5 validates identity, protected targets, declared paths, and consistency before returning a snapshot; the composer still validates its own handoff and never accepts a caller-supplied path, unit syntax, role, or command. It neither broadens privileged read access nor creates an Agent operation. A future Agent exposure must authenticate the peer, establish trustworthy authorization for the exact service and reveal operation, recheck protected-service policy, and keep values out of audit data before invoking the reader.

The permanent precedence, source-ID, secret, and limit rules already live in `docs/systemd-environment-strategy.md`; add only implementation-specific composition evidence or a material limitation there during #39 implementation. Update `docs/code-map.md` only if the new composer is a significant navigation entry. Do not copy permanent rules into a second document.

## Risks / Trade-offs

- [The parser has already collapsed duplicate assignments within each source] -> Count its recorded pre-deduplication `Assignments`, never the final name count; test exact limit and one-over cases with overrides.
- [Copying winners temporarily duplicates secret material] -> Release each parsed input after its contribution is copied, clear replaced output buffers immediately, and dispose all owners on failure. Managed strings and caller-owned source material remain outside this type's erasure guarantee.
- [A malformed handoff could omit a source or mislabel provenance] -> Bind candidate slots to the validated snapshot, verify IDs/counts and value/name correspondence, and reject any mismatch before publication.
- [Real-systemd fixtures could disclose generated values through diagnostics or journal] -> Use the existing private fixture conventions, compare in memory, and assert only non-secret classifications in failure messages.
- [A matching manager observation is not a future-start guarantee] -> Retain M2.5's bounded optimistic-snapshot limitation; #39 performs no new observation or retry.

## Migration Plan

Add the unused internal composer and tests without registering it in Web or Agent. #40 later wires source acquisition, sequential parsing, and composition into `ISystemServiceEnvironmentReader` under one operation deadline. Rollback removes the internal composer and its tests; there is no persisted state, deployment step, IPC contract, or configuration migration.
