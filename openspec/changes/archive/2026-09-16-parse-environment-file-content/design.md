## Context

See `proposal.md` for motivation and `specs/environment-file-parsing/spec.md` for the behavior contract. `Serval.Systemd` currently has an I/O-free `LoadedEnvironmentDecoder` for manager-processed `Environment=` entries and secret-owning `EnvironmentValues` in `Serval.Application`. No environment-file parser or product file reader exists yet. Declaration properties such as the optional-file flag belong to the future D-Bus/file-source composer and are not present in raw file content.

The accepted environment strategy requires exact systemd grammar, fixed byte/record/assignment bounds, value-free failures, immediate disposal of losing candidates, and parity tests on supported real-systemd baselines. This change is a discovery/read building block executed inside `Serval.Systemd`; it adds no privileged operation or filesystem access.

## Goals / Non-Goals

**Goals:**

- Provide a deterministic parser that can later be used by the bounded safe file reader and snapshot composer.
- Reuse the existing secret ownership and metadata model while keeping parser diagnostics value-free.
- Make grammar and resource accounting independently testable without D-Bus or filesystem dependencies.

**Non-Goals:**

- Opening or validating `EnvironmentFile=` paths, following D-Bus declarations, or composing multiple sources.
- Implementing the full `ISystemServiceEnvironmentReader`, Agent IPC, authorization, UI reveal, or audit behavior.
- Expanding shell variables, commands, systemd specifiers, or supporting dotenv extensions.

## Decisions

### Parse raw bytes with a dedicated state machine

Add an internal `EnvironmentFileParser` in `Serval.Systemd` that consumes caller-owned raw bytes, a positive request-local file source ID, the remaining operation assignment allowance, and a cancellation token. It returns a disposable internal success candidate or a value-free failure carrying `InvalidSource`/`LimitExceeded` and the source ID.

The parser will implement explicit record, unquoted, single-quoted, double-quoted, escape, continuation, comment, and comment-escape states. Raw-byte offsets drive source and logical-record accounting; strict incremental UTF-8 decoding validates characters without normalizing the input and additionally rejects U+FEFF and Unicode noncharacters, which valid UTF-8 can otherwise encode but systemd excludes. This keeps systemd grammar visible and auditable.

Alternatives considered: a dotenv package is rejected because its grammar and extension behavior differ; shell sourcing is prohibited because it executes attacker-controlled content; regular-expression parsing is rejected because multiline quoting, continuations, byte accounting, and cancellation require stateful processing.

### Separate validation from secret publication

Use two bounded passes over the immutable caller-owned byte span. The first pass validates the complete source and computes assignment/record accounting without retaining values. The second pass decodes valid assignments into temporary clearable character buffers, transfers copies into `EnvironmentValues`, and clears temporary buffers in `finally` paths. Duplicate `Set` operations clear the previous winning buffer through the existing owner. Metadata is built only after successful parsing, sorted ordinally by name, and every variable uses the supplied source ID.

This mirrors the existing loaded-entry decoder's validate-before-retain behavior and ensures malformed trailing input cannot produce a partial candidate. It costs a second linear scan, accepted in exchange for simpler failure cleanup and a firm all-or-nothing boundary under the 1 MiB source limit.

Alternative considered: decoding the whole file into a managed string is simpler but leaves an immutable copy of every secret outside the clearable owner, so it is rejected.

### Centralize compatible limits without changing public contracts

Move shared constants or introduce an internal limit policy used by both decoders where their semantics overlap. The environment-file parser counts the raw source length, logical records before syntax removal, and all assignments before deduplication. Its assignment allowance is supplied by the future composer so the parser can enforce the operation-wide 16,384 maximum without depending on multi-source composition now.

The parser success exposes internal `SourceBytes` and `Assignments` for future aggregate accounting, plus values and variable metadata whose winning source ID is the supplied request-local ID. It does not construct `EnvironmentSourceMetadata`, because required-versus-optional status comes from the `EnvironmentFiles` declaration rather than file content. The future composer will combine the parser candidate with the validated declaration and construct that source metadata exactly once. The parser does not expose a path or raw declaration, and public application contracts remain unchanged.

Alternative considered: enforce only a per-file assignment maximum, but that would let later composition exceed the accepted operation-wide bound or require reparsing.

### Define a common grammar across supported systemd versions

The compatibility matrix is systemd 249 (Ubuntu 22.04), 255 (Ubuntu 24.04), 257 (Debian 13), and 259 (Ubuntu 26.04). Review of the environment-file parser behavior for those baselines identifies one version divergence in the accepted grammar: with LF-only or CR-only input, before v254 a comment ending in backslash consumes the following physical line as part of the comment, while from v254 onward the next line begins a new record. CRLF is not divergent: v249 consumes the CR in its comment-escape state and the LF ends the comment, while v254 and later end the comment at CR, so both begin a new record afterward.

Serval will use a conservative version-independent subset: any `#` or `;` comment whose last byte before an LF-only or CR-only ending is a backslash is `InvalidSource` on every host. The equivalent CRLF form and other stable comments are ignored normally, and the following line is parsed. The parser will not branch on manager version, so the same bytes always produce the same result and a snapshot cannot vary merely because Serval is moved between supported distributions.

Alternatives considered: emulating the connected manager version would couple an I/O-free parser to external version state and make offline tests/results host-dependent; choosing either pre-254 or post-254 semantics would disagree silently with part of the supported matrix. Conservative rejection makes the incompatibility explicit and value-free.

### Use real systemd as the compatibility oracle

Managed theory tests will exhaustively cover states, malformed cases, forbidden Unicode scalars, cancellation, deduplication, disposal, exact/plus-one limits, the LF-only/CR-only version-divergent comment cases, and stable CRLF. The disposable real-systemd harness will add private synthetic environment files and obtain the manager-parsed environment only as a test oracle, then compare the common subset with the internal parser. Fixtures on 249/255/257/259 will also assert the baseline-specific LF/CR observation, Serval's uniform rejection for those inputs, the common CRLF result, and that their own files remain unchanged.

Alternative considered: rely only on documentation-derived unit tests, but the accepted strategy requires real-systemd evidence for parsing semantics and supported-version compatibility.

## Risks / Trade-offs

- [A subtle grammar difference from systemd could silently change a value] → Encode each quoting/escape state explicitly and require real-systemd parity cases for every supported construction and baseline.
- [Temporary managed buffers can retain secret material if an exceptional path is missed] → Avoid whole-source strings, scope temporary buffers with unconditional clearing, and test failure/cancellation after earlier secret assignments.
- [Two-pass parsing doubles CPU work] → Retain the design because work is linear and strictly bounded to 1 MiB per source and five seconds at the eventual operation level.
- [Future composition may need aggregate limits not represented by this parser] → Accept the remaining assignment allowance now and publish internal exact accounting; total-source bytes remain the future composer's responsibility.
- [A future supported systemd version may introduce another parser divergence] → Gate support on the same fixture corpus and update the explicit compatibility policy before adding that baseline; never choose host-dependent behavior implicitly.

## Migration Plan

Add the parser and tests without registering it in Web or Agent and without changing any runtime call path. Rollback is removal of the unused internal parser and its tests; there is no persisted state or data migration.
