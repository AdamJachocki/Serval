## Context

See [proposal.md](proposal.md) for motivation and [the integration spec](specs/service-environment-reading/spec.md) for behavior. The canonical decisions remain in `docs/systemd-environment-strategy.md`, with identity and protection in the referenced M1 documents. The accepted parser spec and the implemented `read-service-environment-sources` and `compose-service-environment-values` artifacts supply the component contracts.

Currently `SystemdEnvironmentSourceReader.ReadAsync` creates its own deadline, opens a transport, obtains sources, validates stability and disposes observations before returning. `SystemdEnvironmentComposer.Compose` consumes the source snapshot and parsed candidates, releases them, and returns a disposable application result. The real-systemd composition test manually connects these components, but does not provide the production application reader or post-composition consistency checks.

## Goals / Non-Goals

**Goals:** Keep one operation lifetime across all stages; preserve acquisition policy and component ownership contracts; expose the existing application interface with deterministic failure mapping.

**Non-Goals:** Change parser grammar, source precedence, identity policy or numeric limits; create an authorization subsystem, endpoint, public reveal, generic read API, new package, or project. The scope exclusions in the proposal apply.

## Decisions

### Use an application adapter and an internal acquisition session

Add `SystemdServiceEnvironmentReader` in `Serval.Systemd` implementing `ISystemServiceEnvironmentReader`. Its production construction uses fixed transport and file-access implementations; injectable test construction remains internal and cannot skip identifier resolution or protection. Keep all per-operation state local. Do not register it in Web or activate Agent.

Refactor source acquisition into an internal disposable session that owns the transport, initial typed properties, configuration observations and file observations. It supplies the acquired decoded manager/raw-file snapshot and a narrow final-validation operation. It accepts only a service identifier and the operation context, never caller-selected paths or D-Bus members. All construction paths converge on the existing validation/resolution/protection pipeline. The current acquisition-only method may remain as a small compatibility wrapper over the same session, validating before it returns its snapshot; do not maintain two policy implementations.

Alternative: wrap the existing `ReadAsync`, parse and compose after it returns. Rejected because its timer, descriptors and transport have already ended. Alternative: reacquire everything after composition. Rejected because it loses the original descriptor bindings and duplicates sensitive reads.

### Separate observation lifetime from secret-input ownership

Transfer owned raw bytes out of file observations into the existing source snapshot while retaining handles and observation metadata in the session. The composer continues to own and dispose its snapshot and parser candidates on invocation, including failure. The session retains no borrowed reference to a buffer the composer may clear; final validation uses retained properties and filesystem metadata/handles. Manager-property strings retained for comparison stay private, bounded and unlogged; ordinary managed strings are not promised to be erasable.

Before invoking composition, the adapter owns all parser candidates and the source snapshot and disposes them on partial parse failure. After composition returns a `Success`, the adapter owns that result provisionally. It calls final session validation, releases acquisition resources, and performs the final caller-cancellation/deadline check before transferring the result to the consumer. A `finally` path disposes any result not transferred, including when validation, cleanup or the final check fails. Cleanup must preserve the primary controlled failure/cancellation and never expose raw exception text; define narrow safe handling for expected disposal failures rather than returning success with unclosed resources. Repeated disposal remains safe.

Alternative: make the I/O-free composer perform asynchronous filesystem validation. Rejected because it would mix boundaries. Constructing an application `Success` internally does not publish it: publication is only returning it from the adapter after validation.

### Own the deadline at the outer operation boundary

Use an internal operation context containing the original caller token, one linked cancellation source, a `TimeProvider` timestamp and the fixed deadline. Start it at reader entry after basic argument validation and before identity/protection work. Check elapsed monotonic time as well as cancellation at controlled exits and before publication so timer callback scheduling cannot admit a late result. The context supplies a decreasing remaining duration where the transport needs a duration, and the same linked token through asynchronous and bounded synchronous work. A transport-local timer must never extend the outer deadline; the outer context remains authoritative. No per-file or parser timer is introduced, and M1 transport consumers retain their behavior.

Map cancellation at the adapter boundary: observed original caller cancellation throws `OperationCanceledException` with that token, otherwise an elapsed outer budget gives `Timeout`. Recheck this priority before returning any controlled failure, including failures arriving late from a dependency. Preserve genuine transport failure classification when the outer deadline has not elapsed. This keeps the parser's cancellation-only contract and the existing managed five-second operation policy. Non-cancellable kernel calls retain the documented limitation; no helper process or hard termination guarantee is added.

Alternative: a fresh timeout for each stage or file. Rejected because total runtime would grow with source count. Removing the total budget would change M2.1 policy and is outside #40.

### Orchestrate existing parsers and map failures without reinterpretation

Start remaining assignments at `MaxAssignments - snapshot.ManagerSource.Assignments`. Parse each present occurrence with that remaining allowance; subtract its pre-deduplication `Assignments` on success. Preserve a null slot only for optional missing occurrences. Invoke the existing composer with the complete ordered candidate list and linked token. Retain its defensive aggregate checks. Do not add another unit parser or source-order mechanism.

Copy acquisition failure code/reason/source ID directly into the application failure contract; preserve parser and composition `InvalidSource`/`LimitExceeded` classifications. Keep safe argument/programming errors distinct from expected environmental failures rather than swallowing every exception as transport failure. Final-validation disappearance or mismatch is inconsistent; transport failure remains transport failure. Metadata comes from the existing result, and `EnvironmentValues` remains inaccessible for public reveal and excluded from default result serialization. No new automatic whole-result serialization API is needed.

Alternative: derive counts from final variable names or deduplicate repeated paths. Rejected because both undercount work and change provenance.

### Keep the future privileged boundary explicit

Untrusted inputs are the requested identifier, manager fields, file paths/content and concurrent filesystem changes. This change integrates the already accepted narrow read capability for one service; restricted sources may require future Agent privileges. It introduces no active root process or IPC operation. The smallest public request stays `SystemServiceId` plus cancellation, with the existing success/failure response.

The library enforces identity and protected policy independently of Web. It does not know a principal and cannot grant user authorization. The future Agent must authenticate its peer, establish trustworthy delegation and enforce `Service.View` and `Environment.Reveal` before any value-bearing invocation; caller-supplied role/authorization flags are not evidence. No unauthenticated runtime endpoint is added, so principal-denial tests belong to that future integration. This change tests direct-call protection, malicious identifiers, aliases and privileged instances.

Reuse typed fixed D-Bus calls and validated no-follow source access to contain injection, traversal, symlink, substitution and confused-deputy risks. No generic command, arbitrary-path read, write, reload or restart is exposed. Future audit remains value-free; no audit storage is added here. Update the canonical environment strategy with session lifetime, publication and ownership details and the code map with the new adapter, without duplicating permanent policy in another document.

## Risks / Trade-offs

- [Handles stay open through parsing and composition] -> Existing bounded source counts and the shared deadline constrain retention; exercise cleanup after each stage.
- [Composer clears inputs needed for validation] -> Separate buffer ownership from observations and test final validation after composition disposal.
- [Late cancellation loses to another failure] -> One outer priority check and deterministic clock/stage tests, including cleanup and result construction.
- [Optimistic checks miss a change that reverts between observations] -> Preserve the documented non-atomic limitation; no retries or stronger snapshot claim.
- [Local Linux evidence is mistaken for platform coverage] -> Report managed tests, local systemd and CI matrix separately.
- [OpenSpec ignores the existing malformed config YAML] -> This proposal uses the default schema and directly read canonical guidance. Config repair is separate from these planning artifacts; record the warning in validation evidence.

## Migration Plan

Implement on the completed #38/#39 baseline and keep their regression suites. Add contract-level consumer tests and extend the existing disposable integration harness to invoke the application reader. Run repository gates and independent review. There is no persisted data migration or deployment activation. Rollback removes the new adapter and restores internal acquisition orchestration without changing the public application contract or ordinary inventory.
