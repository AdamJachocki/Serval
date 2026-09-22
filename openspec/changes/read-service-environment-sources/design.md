## Context

See [proposal.md](proposal.md) for motivation and scope. The accepted strategy in `docs/systemd-environment-strategy.md` defines the behavior table, numeric limits, secret-handling rules, and optimistic snapshot algorithm. M1 already provides validated `SystemServiceId`, canonical/alias resolution, protected-service classification, and Serval privileged-unit exclusion. M2.2–M2.4 provide secret-owning values, manager-entry decoding, and environment-file parsing.

Issue #38 is deliberately between those pieces: it acquires a stable ordered input snapshot but does not parse file bytes, compose cross-source values (#39), or expose the application reader (#40). The capability remains internal to `Serval.Systemd` and is not registered in `Serval.Web` or the inert `Serval.Agent`.

The existing D-Bus transport exposes only discovery/inspection metadata. Its narrow fixed-member shape must be preserved while adding the properties required by M2. The filesystem side is new: declared administrator files may live outside Serval-owned roots and may require root access in the future Agent, so a simple path allowlist or ordinary `File.Open` is insufficient.

## Goals / Non-Goals

**Goals:**

- Produce one disposable internal snapshot containing the canonical service identity, decoded manager contribution, and ordered present/missing file-source occurrences with request-local IDs.
- Reject ineligible or unsupported targets before opening environment files.
- Bind every read to manager-declared paths and resist traversal, symlinks, special files, pseudo-files, substitution, mutation, and unbounded resource use.
- Make manager protocol, orchestration, and Linux file behavior independently testable while retaining real-systemd coverage.
- Preserve the accepted safe failure taxonomy and clear unpublished secret buffers on every non-success exit.

**Non-Goals:**

- Parse environment-file bytes or compute precedence and winning values across sources.
- Implement `ISystemServiceEnvironmentReader`, Agent IPC, principal authentication, authorization, audit, Web registration, UI serialization, or value reveal.
- Read unit/drop-in contents, recreate systemd selection rules, reload PID 1, mutate any source, or perform service lifecycle operations.
- Provide an atomic system-wide or future-start snapshot guarantee.

## Decisions

### Introduce an internal acquisition result separate from the application result

Add an internal source-reader contract in `Serval.Systemd` accepting only `SystemServiceId` and `CancellationToken`. Its closed result has:

- success: canonical identity, decoded manager source ID 0, and an ordered collection of environment-file occurrences with IDs 1..N, optional/missing metadata, and owned raw bytes for present files;
- failure: only the existing safe failure code, optional defined unsupported reason, and optional request-local source ID.

The success and each secret-bearing component are disposable. Construction validates consecutive IDs, ordering, missing/optional invariants, and ownership. No path is retained in publishable metadata or `ToString`/serialization. Internal acquisition state may retain validated paths and handles only until final consistency validation, then releases them before returning success.

This boundary supplies exactly what #39 and #40 will need without pretending that raw files are already parsed or that values are already composed.

Alternative considered: implement `ISystemServiceEnvironmentReader` now. Rejected because it requires file parsing and multi-source composition owned by #39/#40 and would collapse the intended issue boundaries.

Alternative considered: return ordinary strings or byte arrays without ownership. Rejected because error, cancellation, and retry paths would retain secret material with no deterministic clearing contract.

### Reuse one identity resolver and enforce protection before value acquisition

Extract or reuse the M1 name-resolution flow shared by inspection rather than implementing a second alias algorithm. Resolution validates the request again at the adapter boundary, rejects a template without an instance, connects only to the system manager, validates the manager baseline, resolves the opaque unit reference, then constructs one authoritative identity from `Id` and `Names`.

Protection is evaluated across the request, canonical ID, and all validated aliases in both directions. `BuiltInProtectedServices` produces `ProtectedTarget`; `ServalPrivilegedUnits` remains unconditionally excluded. Only after these checks pass may the reader request `Environment` or open source files.

Alternative considered: trust the requested identifier after `ListUnitsByNames`. Rejected because an alias could bypass canonical protected-family classification.

### Extend D-Bus with fixed typed environment-property reads

Extend the existing protocol, proxy XML/source, and transport with fixed methods for the accepted Unit properties (`Id`, `Names`, `LoadState`, `FragmentPath`, `DropInPaths`, `NeedDaemonReload`, `Transient`, `UnitFileState`) and Service properties (`Environment`, `EnvironmentFiles`, `UnsetEnvironment`, `PassEnvironment`). Service properties are read from the opaque unit reference issued by the same transport.

Each property uses its exact expected signature and independent collection/string/path bounds. The proxy enforces fixed item-count, per-item UTF-8 byte, and aggregate encoded-byte bounds while streaming the reply body, before constructing the next managed string or array element. `Names` retains the separate M1 identity bound; the 65-source M2 bound does not limit aliases. The transport exposes no property-name parameter, `GetAll`, arbitrary object path, or generic method call. A dedicated immutable property snapshot makes complete initial/final comparison explicit. A required `UnknownProperty` or `UnknownInterface` during the initial snapshot maps to `UnsupportedConfiguration/UnsupportedProperty`; disappearance after identity resolution maps to `InconsistentSnapshot`. Wrongly typed, oversized, malformed, or otherwise inconsistent replies map to a safe controlled failure; raw remote payloads and error text are discarded.

Alternative considered: use `Properties.GetAll` once. Rejected because it broadens the secret-bearing response and makes forward compatibility dependent on unrelated properties.

Alternative considered: parse `systemctl show` or unit files. Rejected because it creates a command boundary and duplicates manager interpretation of aliases, drop-ins, resets, and specifiers.

### Validate unsupported cases before opening declared files

After the first complete typed property snapshot, validate in this order:

1. authoritative identity, concrete load state, and known aliases;
2. protected/privileged target policy;
3. dirty, transient, generated, active `UnsetEnvironment`, active `PassEnvironment`, missing property, pattern, unresolved-specifier, and limit conditions;
4. fragment/drop-in metadata paths;
5. environment-file declarations.

The order provides a testable guarantee that denial and unsupported configuration do not trigger file-content access. Empty post-reset lists are accepted. Generated path classification uses component-boundary checks for the accepted generator directories; similarly named siblings are not classified as generated. Ordinary stable runtime paths under `/run/systemd/system` remain supported.

Alternative considered: open sources while D-Bus properties are streamed. Rejected because unsupported configuration could cause privileged reads before the full policy decision exists.

### Use descriptor-relative, no-follow Linux path resolution

Implement a small internal Linux file-access boundary rather than exposing a generic path reader. It receives only paths already obtained and validated by the orchestration layer. Path syntax requires an absolute bounded UTF-8 path, no NUL/control character, empty interior component, `.`/`..`, glob metacharacter, or unresolved `%`.

Open from a descriptor for `/` and resolve components with Linux no-follow semantics. Prefer `openat2` with `RESOLVE_NO_SYMLINKS | RESOLVE_NO_MAGICLINKS` and a beneath/in-root constraint appropriate to an absolute path rewritten relative to the root descriptor. Keep a component-wise `openat` fallback only if it provides the same no-follow and directory-descriptor guarantees on every supported baseline; otherwise fail closed as unsupported. The final open uses nonblocking, close-on-exec, no-follow semantics before content is consumed. `statx` must identify a regular file and report every device/inode/size/mode/mtime/ctime field consumed by Serval; a zero return with a partial mask is unavailable. `fstatfs` rejects process and other explicitly unsafe pseudo-filesystems while still allowing supported administrator sources on ordinary filesystems and `/run` tmpfs. FIFO, socket, and device types are rejected before reading.

Fragment and drop-in paths use the same no-follow resolver in metadata-only mode. Their content is never read. Open descriptors are retained through final validation so `fstat` can compare the same objects; path re-resolution verifies the final path-to-device/inode binding. The implementation never performs a check-by-path followed by an unrelated path reopen for content.

Alternative considered: `FileInfo` followed by `File.OpenRead`. Rejected because it follows links by default and introduces a check/use race.

Alternative considered: restrict reads to `/etc/serval`. Rejected because the capability reads existing administrator declarations and the accepted model includes legal sources elsewhere, including `/run`.

### Stream bounded bytes into clearable ownership

Read present files from the already validated descriptor in bounded chunks directly into an internal clearable byte owner. Pass the smaller of the per-source and remaining aggregate budget into each file acquisition, and request at most one byte beyond that remaining budget to distinguish EOF from the first excess byte. Stop at 1 MiB for one occurrence or 4 MiB across the manager contribution and every file occurrence; repeated paths count and are reread as separate source occurrences. Only initial `ENOENT` for an optional declaration creates missing metadata. Initial required absence maps to `SourceUnavailable`; permission and other I/O failures remain failures. A disappearance after initial observation is inconsistent.

The existing manager decoder validates and owns the loaded `Environment` contribution, including its one-source byte, entry, encoding, and cancellation rules. File bytes remain unparsed in this change, so the 16,384 total file-assignment limit is intentionally deferred to parsing/composition. Acquisition still enforces the manager decoder's 16,384-entry limit.

On any exception or controlled failure, a single candidate owner disposes the decoded manager values and all file buffers in reverse acquisition order. Successful ownership transfers once to the returned snapshot.

Alternative considered: call the environment-file parser while reading. Rejected because #38 explicitly acquires sources independently of parser availability and #39/#40 own parsed composition.

### Validate an optimistic snapshot once, without retry

One linked five-second managed-operation deadline covers connection, resolution, property reads, metadata probes, content reads, and validation. Pass its token to every asynchronous operation and check it immediately before and after each synchronous Linux filesystem call. `open`, `openat2`, `statx`, and `fstatfs` do not accept cancellation or a deadline, so this contract does not claim to interrupt an in-progress syscall or strictly bound wall-clock time while the kernel is blocked. When such a call eventually returns after cancellation or deadline expiry, release any acquired descriptor, discard the result, clear the unpublished candidate, and report caller cancellation or `Timeout` before starting another step or publishing success.

Capture the initial typed property snapshot; device/inode/size/mtime/ctime for fragment, drop-ins, and present source descriptors; and stable absence for optional missing sources. After reading:

1. read the exact typed properties again;
2. compare canonical identity and every contract property;
3. compare descriptor metadata before/after;
4. resolve each path again and compare its binding, including continued absence;
5. perform a final caller-cancellation/deadline check before ownership transfer.

Any detected difference yields `InconsistentSnapshot`; there is no retry loop. Initial absence is `NotFound`/`SourceUnavailable` as applicable, while service or required-property disappearance after a successful initial observation is inconsistent. Recheck both cancellation sources immediately before returning any controlled failure, including one produced by a dependency that returned an ordinary error after cancellation. Caller cancellation wins whenever its token is signaled; otherwise deadline expiration maps to `Timeout`.

Alternative considered: retry until two reads match. Rejected because churn could consume unbounded privileged time and a matching retry still would not create an atomic global snapshot.

Alternative considered: run filesystem access in a killable helper process to provide a stricter wall-clock bound. Rejected for this internal acquisition change because it would add a process boundary, packaging and lifecycle surface disproportionate to the rare blocked-filesystem case, while issue #38 introduces no runtime consumer. A future Agent design may revisit isolation if operational evidence requires a hard deadline.

Alternative considered: return at the deadline while abandoning a blocked in-process worker. Rejected because repeated stalls could accumulate unreclaimable threads and descriptors and create a denial-of-service condition.

### Privileged-boundary analysis

Attacker-controlled inputs are the requested service identifier, cancellation timing, all D-Bus replies, every manager-returned path and declaration, file bytes, filesystem metadata, and future IPC correlation metadata. There is no caller-supplied path, D-Bus property, command, role, group, actor, or authorization flag.

The exact future privileged capability is: read the fixed environment-source declarations for one validated, eligible system service, including administrator files that may be root-readable. Root may be required solely for those declared reads and metadata checks. The operation does not write, execute, reload, restart, or expose arbitrary file/D-Bus access.

Issue #38 creates only the internal library mechanism. It has no IPC request/response and no trustworthy principal or authorization basis yet. A future Agent operation must authenticate its local peer, bind a verifiable principal/delegation, enforce the exact service-scoped reveal permission and protected-target policy before invoking this reader, and audit only non-secret actor/operation/canonical-service/outcome/correlation metadata. A caller-provided `IsAuthorized`, actor name, or group list can never supply that trust.

Threat controls are:

- command injection: no process or shell path exists;
- arbitrary D-Bus/confused deputy: fixed endpoint, object references, members, and signatures only;
- arbitrary service targeting/protected bypass: bounded `SystemServiceId`, canonical/alias verification, protected-family checks before value reads;
- traversal, magic links, symlinks, and TOCTOU: validated manager-only paths, descriptor-relative no-follow resolution, retained handles, and binding revalidation;
- special-file blocking and pseudo-file reads: nonblocking open, file-type and filesystem checks before content reads;
- resource exhaustion: fixed counts/bytes/path bounds, streaming cutoffs, one monotonic managed deadline, cancellation-aware asynchronous I/O, and cancellation checks around synchronous filesystem calls; an in-progress blocking kernel syscall remains an explicitly documented availability limitation;
- secret disclosure: owned clearable buffers, value-free failures, sanitized diagnostics, and no persistence/logging/serialization;
- arbitrary write/lifecycle action: no write flags, mutation API, reload, or lifecycle member is present.

## Risks / Trade-offs

- [Optimistic validation cannot prove a globally atomic snapshot or prevent an undetectable change-and-revert] → Document this limit in the internal contract and public strategy; fail on every detectable change and never claim future-start consistency.
- [Linux syscall behavior differs across kernels and filesystems] → Exercise the supported Ubuntu/Debian systemd matrix and focused Linux filesystem tests; fail closed when equivalent no-follow guarantees are unavailable.
- [A synchronous filesystem syscall can remain blocked after the managed deadline because Linux exposes no cancellation parameter] → Check cancellation before and after every call, release a late result without publishing it, document that the deadline is best-effort during the syscall, and avoid abandoned in-process workers; revisit a killable helper only if a future exposed Agent operation requires a strict wall-clock guarantee.
- [A pseudo-filesystem can present regular-looking nodes] → Combine final mode checks with filesystem-type rejection for known unsafe pseudo-filesystems and add `/proc` negative tests while preserving supported tmpfs runtime sources.
- [Holding descriptors and secret buffers increases short-lived resource use] → Enforce 65-source/4-MiB bounds and the five-second managed deadline, dispose in one owner, and release all descriptors immediately after final validation or after observing cancellation.
- [Extending the existing D-Bus transport can accidentally broaden discovery behavior] → Add separate fixed typed reads and regression-test the existing discovery/inspection contract; do not expose generic property selection.
- [Config parsing currently warns that `openspec/config.yaml` is malformed] → The proposal follows the repository's accepted documents and issue contract directly; repairing unrelated OpenSpec configuration is outside this change.

## Migration Plan

1. Add internal result ownership and the fixed typed D-Bus property contract without registering a runtime consumer.
2. Add Linux safe-resolution and bounded-read primitives with focused negative tests.
3. Add the source orchestration and consistency validation behind the internal contract.
4. Extend disposable real-systemd fixtures and the supported CI matrix for the required identity, ordering, runtime, and unsupported cases.
5. Leave production composition unchanged until #39 and #40 explicitly consume this internal capability.

Rollback removes the unused internal reader and its transport/file additions; there is no stored data, schema migration, runtime registration, source mutation, or external API to unwind.
