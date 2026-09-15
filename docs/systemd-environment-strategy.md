# Systemd environment read strategy

Status: Accepted for M2 implementation  
Date: 2026-09-14  
Issue: [#34](https://github.com/AdamJachocki/Serval/issues/34)

## Model and ownership

One read accepts only `SystemServiceId` and `CancellationToken` through
`ISystemServiceEnvironmentReader.ReadAsync`. Application contracts live in
`Serval.Application`; existing service identity remains in `Serval.Domain`.
All future D-Bus, parsing, file access and composition stay in `Serval.Systemd`.
M2.1 introduces no adapter, endpoint, IPC, ACL, database, parser or root operation.
The library is reserved for the future Agent; never register it in Web.

`SupportedDeclarations` means the complete result of the manager-loaded
`Environment` property plus current contents of its declared `EnvironmentFiles`.
It is never the complete environment of a running or future process. Do not read
`/proc`, execute generators, start a unit or simulate PAM/ExecStartPre.
There is no incomplete-success flag. Empty declarations and empty files are valid
successes. Any failed required step discards the whole candidate snapshot.

`Success` contains canonical identity, the fixed model scope, immutable source
and variable metadata, and a separate `EnvironmentValues` component. Values have
no public enumerable/property representation; access requires `Reveal(name)`.
Default JSON serialization excludes the success's values component; its own
serialization exposes no values. `ToString` is redacted. This is accidental-leak
protection, not authorization or a secure-memory guarantee: managed strings may
remain in memory. Do not log objects via private-field reflection or call Reveal
from diagnostic code. No environment value may enter logs, audit, SQLite,
telemetry, exception messages, snapshots or test failure output.

Sources use request-local integer IDs: 0 is the aggregate manager Environment
source; file declarations use 1..N in manager order, including repeated paths.
Source order in the result is that order. Optional absent sources remain as
metadata with `IsMissing=true`. Metadata contains no paths, raw declarations,
hashes of secrets or D-Bus error messages. Variables are ordinally sorted by name;
each records the winning source ID. No historical losing values are retained.
The manager aggregate cannot identify the original unit/drop-in line for an
Environment assignment; do not invent that provenance. Source IDs deliberately
do not promise stable identity between reads. Raw paths remain adapter-internal.
The success constructor copies collections, rejects duplicate identities/names,
unknown or missing winning sources, and any mismatch between names and values.
It validates structure; only the adapter can establish that every source was read.

## Manager reference and snapshot algorithm

Reuse the accepted [discovery identity strategy](systemd-discovery-strategy.md)
and M1 identity/protection code, not a second filesystem-wide discovery engine.
Only connect to the system manager. No `systemctl cat`, shell, arbitrary command,
generic D-Bus member supplied by a caller, or unit-text merge fallback is allowed.

1. Start one monotonic 5-second deadline, linked with caller cancellation. Require
   the M1 systemd baseline (manager Version, minimum 249). Validate
   the concrete system service identifier, resolve its canonical Id and Names,
   and apply protected/Serval privileged-family checks in both alias directions,
   including template-derived instances. Never treat a template as an instance.
2. Read only the allowlisted typed Unit properties `Id` (s), `Names` (as),
   `LoadState` (s), `FragmentPath` (s), `DropInPaths` (as), `NeedDaemonReload` (b),
   `Transient` (b), `UnitFileState` (s), and Service properties `Environment` (as),
   `EnvironmentFiles` (a(sb)), `UnsetEnvironment` (as), `PassEnvironment` (as).
   Resolve concrete unloaded units through M1 mechanisms; do not start them.
   The boolean in each EnvironmentFiles tuple is optional/ignore-errors state.
   Serval deliberately ignores only initial ENOENT; other optional-source errors
   fail closed, even if systemd would ignore them.
   Do not request broad GetAll replies carrying unrelated secrets.
3. Reject unsupported cases below before opening files. `NeedDaemonReload=true`
   is `InconsistentSnapshot`, never permission to reload. A missing or wrongly
   typed required property is `UnsupportedConfiguration/UnsupportedProperty`;
   never assume a missing property is empty. Validate all returned IDs and paths.
4. Capture identity and all listed properties, plus device/inode/size/mtime/ctime
   for the declared fragment and drop-ins without reading their contents. These
   bounded paths are used only for consistency, never as a second source of
   Environment assignments. Unavailable configuration metadata fails closed.
5. Decode the loaded Environment array (already processed by the manager), then
   read each declared file through a bounded safe file reader. Capture descriptor
   metadata before/after each read; retain handles until final validation. Apply
   the precedence and parser rules below into a private candidate result.
6. Re-read the exact properties, configuration metadata, file descriptor metadata
   and path-to-inode bindings (including absence for optional missing files).
   Any difference or dirty manager state yields `InconsistentSnapshot`. No retry
   or implicit reload. A transport failure remains a transport failure.
7. Publish one success only after validation and a final cancellation/deadline
   check. Drop candidate values on all other exits.

This is a bounded optimistic observation, not an atomic transaction across PID 1
and the filesystem. Concurrent changes that revert between observations cannot
be ruled out; no point-in-time or future-start guarantee is made. Ordinary unit
edits pending daemon-reload are inconsistent, while stable edits to declared
environment files intentionally affect the next read without daemon-reload.

## Binding behavior table

Every supported row requires the named unit and real-systemd tests when its
implementation lands (M2.3–M2.7); these are not claims of tests already run in
M2.1. Rejections must be tested before file access and without partial values.

| Construction | M2 decision | Required test |
| --- | --- | --- |
| Loaded Environment assignments, duplicates, empty values and reset directives | Supported using the manager's final array; split each entry at its first `=`; no second unit-syntax unquoting | Multiple directives, reset in drop-in, duplicate and empty assignment |
| Unit/drop-in lexical precedence | Supported through manager-loaded properties; no reimplementation | Base plus ordered drop-ins and reset |
| Multiple EnvironmentFiles and repeated path declarations | Supported in returned order; last assignment wins within/across files; files override Environment | Duplicate key across all three sources and repeated path |
| Optional absent regular file | Supported, empty contribution with missing metadata; only ENOENT is ignored | Absent optional succeeds; permission error fails |
| Required missing file or unreadable optional file | `SourceUnavailable` | Missing required, EACCES, I/O failure |
| Absolute literal file path | Supported subject to the safe path rules below | Normal regular file, root-directory service uses manager filesystem |
| Glob syntax `*`, `?`, `[` or `]` in returned file path | `UnsupportedConfiguration/PathPattern`; no glob expansion | Matching, nonmatching and escaped-looking patterns |
| Specifiers expanded by manager in Environment or EnvironmentFiles | Supported; never expand a second time | `%n`, `%i`, `%%` on a concrete instance on each baseline |
| Percent remaining in returned file path, including literal percent from `%%` | `UnsupportedConfiguration/UnresolvedSpecifier` (deliberately conservative) | Returned percent path rejected before open |
| Percent or dollar in returned Environment values or file contents | Supported as literal data, no shell/variable/specifier expansion by Serval | Literal `%`, `$`, command-looking text |
| Active UnsetEnvironment, by name or name=value | `UnsupportedConfiguration/UnsetEnvironment` | Both forms reject; empty reset accepted |
| Active PassEnvironment | `UnsupportedConfiguration/PassEnvironment` | Set/unset manager variable both reject; empty reset accepted |
| Transient unit | `UnsupportedConfiguration/TransientUnit` | Transient true rejected |
| Generated unit or generated fragment/drop-in | `UnsupportedConfiguration/GeneratedUnit` | UnitFileState generated and paths in all generator directories |
| Ordinary runtime fragment/drop-in in /run/systemd/system | Supported if stable and not transient/generated | Stable runtime drop-in and concurrent replacement |
| Declared regular environment file in /run | Supported current contents under identical path and consistency rules | Stable file, disappearing file, content mutation |
| DefaultEnvironment, manager runtime environment, kernel settings | Outside model; never query/merge them | Fixture documents their absence from declarations result |
| PAM, service code/ExecStartPre, generators executed later, credentials, automatic HOME/USER/PATH and activation variables | Outside model; never execute or predict them | Model scope remains SupportedDeclarations |
| RootDirectory/RootImage/mount namespace of executed service | Outside model; declared environment files are read in manager's filesystem view | Host file vs service-root file |
| Unsafe path/symlink/special file | `UnsupportedConfiguration/UnsafePath` | Traversal, symlink in each component, FIFO, device, socket |
| Unknown required property/signature or unverified manager semantics | `UnsupportedConfiguration/UnsupportedProperty` | Missing property and wrong signature |

Generated path classification covers `/run/systemd/generator`,
`/run/systemd/generator.early`, `/run/systemd/generator.late` and descendants with
component boundaries, not similarly named sibling directories. If classification
cannot be established, reject instead of guessing. New directives that transform
the modeled declarations require an explicit strategy amendment before support;
they cannot silently be classified outside scope. Unknown unrelated properties
do not require reading the entire property set.

## Parsing and file policy for later implementations

Use the documented systemd EnvironmentFile grammar, not dotenv or a shell:
UTF-8, comments/blank lines, first assignment separator, unquoted/single-quoted/
double-quoted values, continuation and multiline quoting. Whitespace trimming and
backslash rules must match each quoting mode. Reject malformed encoding, NUL,
invalid variable names, invalid assignments and unterminated quoting as
`InvalidSource`; no line may be silently dropped except grammar-defined comments,
blank lines and non-assignment lines (which systemd ignores). Names use ASCII
`[A-Za-z_][A-Za-z0-9_]*`. No interpolation, command execution or export syntax.
Empty value differs from absent variable. Repeated assignment replaces the value
and winning provenance. Discard losing values promptly.

Environment paths must be absolute, bounded UTF-8, without NUL/control characters,
empty interior components, `.` or `..`; reject rather than normalize ambiguity.
Open from the manager filesystem root with component-by-component no-follow
resolution, refuse symlinks in every component and require a regular final file.
Do not open FIFO/devices to discover their type: use nonblocking/type-safe handles.
Revalidate descriptor and path identity to resist substitution. No writes occur.
The same no-follow metadata policy applies to fragment/drop-in consistency paths;
an unsupported symlink configuration is rejected rather than followed implicitly.
Missing/unreadable paths after a successful initial observation imply
`InconsistentSnapshot`; initial required-path failure is `SourceUnavailable`.

## Fixed resource limits

Limits are internal policy, not request options. Equality is allowed; the next
byte/item fails `LimitExceeded`. Count before allocation where possible and stop
streaming as soon as the bound is exceeded. D-Bus decoding must be bounded too.

| Resource | Inclusive maximum | Accounting and rationale |
| --- | ---: | --- |
| One source | 1,048,576 bytes (1 MiB) | Raw file bytes, or sum of UTF-8 bytes of loaded Environment entries; prevents one giant allocation |
| All sources | 4,194,304 bytes (4 MiB) | Manager aggregate plus every file occurrence, including repeated paths; bounds total secret material |
| Sources | 65 | One manager aggregate plus 64 file declarations, even missing optional ones; bounds opens |
| Assignments | 16,384 | All parsed assignments before deduplication, including overridden ones; bounds CPU and metadata |
| Logical line | 65,536 UTF-8 bytes | Bytes accumulated for one logical record including quoting/continuation syntax before decoding; manager entry also counts as one record |
| Path | 4,096 UTF-8 bytes | Each returned file/fragment/drop-in path; bounds path processing |
| Configuration paths | 129 | One fragment plus 128 drop-ins; bounds consistency probes |
| Whole operation | 5 seconds | Includes identity/protection, D-Bus, file access, parsing and final checks; no per-source reset or retries |

Every byte/count limit requires an exact-boundary success and boundary+1 failure
test, including repeated sources, overridden assignments, multibyte UTF-8 and
continuations. Deadline tests use a controlled clock: just-before completion,
deadline reached (Timeout), and caller cancellation racing deadline (cancellation
wins). A blocking read must not survive cancellation/deadline indefinitely.

## Failure and trust boundary

`Failure` exposes only an enum code, an optional defined unsupported-reason enum,
and an optional request-local source ID. UnsupportedConfiguration requires its
reason; other failures cannot carry one. Source ID is null for unit-wide errors.
Never attach inner exceptions, paths, source snippets, assignments or partial
maps. Distinguish `NotFound` (absent concrete unit), `ProtectedTarget`,
`UnsupportedConfiguration`, `InvalidSource`, `SourceUnavailable`,
`InconsistentSnapshot`, `LimitExceeded`, `Timeout`, and `TransportError` (bus
disconnect, protocol failure or remote failure not mapped above). Invalid caller
arguments remain safe argument exceptions. Caller cancellation propagates as
OperationCanceledException with its token, never a failure code or Timeout.

All request identities, D-Bus fields and file bytes are attacker-controlled.
Future capability: narrowly read declared environment sources for one validated
service; root may be needed for restricted files. M2 does not activate this
capability or implement user authorization. M5 must authenticate IPC peer and
bind trustworthy delegation, resolve identity, enforce protected policy plus
Service.View and Environment.Reveal BEFORE querying value-bearing properties or
opening sources. Web-supplied identity, group lists and IsAuthorized are invalid
trust evidence. Policy failure denies access; Web admin cannot override it.
Audit only actor, canonical service, operation, outcome, correlation and variable
names where relevant; never values or raw payloads. Masking belongs to future UI.

Typed fixed members prevent command injection; no caller path prevents generic
file-read APIs. Canonical and alias checks prevent targeting bypass. Descriptor
validation bounds traversal/symlink/TOCTOU exposure; limits and deadline bound
resource exhaustion. There is no arbitrary write, reload or restart capability.
Future integration tests must cover permitted, unauthorized, malicious identity,
protected aliases/instances and similarly named controls before any source read.

## Evidence and rollout gates

Semantics reference: [systemd.exec](https://raw.githubusercontent.com/systemd/systemd/main/man/systemd.exec.xml),
[systemd.unit](https://raw.githubusercontent.com/systemd/systemd/main/man/systemd.unit.xml),
and [typed D-Bus baseline source](https://raw.githubusercontent.com/systemd/systemd/v249/src/core/dbus-execute.c).
The [v249 directive loader](https://raw.githubusercontent.com/systemd/systemd/v249/src/core/load-fragment.c)
confirms specifier processing before property access. The older v249 manual's
brief escaping description is not a mandate to add C-escape decoding to files;
use the explicit quoting rules in the current manual and verify the supported
cases against each baseline's real parser.
The precedence and manager-processing decisions above follow these references;
the conservative rejection policy and numeric limits are Serval decisions.

M2.1 tests contract shape, empty success, absence, failure disjointness, mismatched
metadata rejection and accidental secret exposure. No new systemd behavior is
implemented here. M2.2–M2.7 must introduce their negative/unit tests and real
systemd cases with implementation, not defer them to M2.8. Confirm the typed
properties, specifier stage, resets, precedence and file grammar on the existing
249/255/257/259 supported CI matrix, x64 and ARM64. No compatibility pass is
claimed by this design document. Use generated synthetic values without printing
them, unique disposable identities, manager-wide collision checks and cleanup;
never shadow Serval units. M2.8 adds end-to-end coverage. Each task still requires
formatting, warnings-as-errors build, applicable tests and fresh independent review.
