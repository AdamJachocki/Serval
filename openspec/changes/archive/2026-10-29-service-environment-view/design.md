# Design

## Context

See proposal.md for motivation and capability scope. The main-branch Web details page uses `InspectService`; grant creation hard-codes `Service.View`. `AgentDispatcher` already resolves current Linux identities, canonical services and aliases. `SystemdServiceEnvironmentReader` implements bounded supported-declaration reads, but is not registered in Agent. `EnvironmentValues.Reveal` is internal and ordinary serialization is redacted. `AgentProtocol` currently assumes non-secret service results and does not clear its result-write buffer. SQLite schema version 1 constrains grant operations to `Service.View`, and `AuditEntry` has no variable name.

Use ARCHITECTURE.md, SECURITY.md, docs/identity-deployment.md and docs/systemd-environment-strategy.md as the canonical boundaries. This design adds a deliberate disclosure path; it does not replace the existing source acquisition or composition algorithms.

## Goals / Non-Goals

**Goals:** Connect the existing reader to narrow authorized operations, keep secret ownership explicit across IPC/HTTP, and make metadata access independently grantable.

**Non-Goals:** A generalized secret API, reader expansion to unsupported systemd features, source-path disclosure, cached snapshots, writes or service lifecycle actions. No dependency on the separate visual redesign branch.

## Decisions

### Operation boundaries and permission semantics

Add `ListEnvironment(session, correlationId, service)` and `RevealEnvironmentVariable(session, correlationId, service, name)` to the Application protocol and Agent dispatcher. Metadata returns canonical identity, SupportedDeclarations scope, existing source/variable metadata and a current `canReveal` hint. Reveal returns exactly one value plus its name, canonical identity, scope and current source label (kind and request-local ordinal, not a path). Never combine source IDs from different reads as stable identities.

The metadata operation requires Environment.View; reveal requires Environment.View and Environment.Reveal. Service.View continues to govern entry to the existing service-details page and inventory; it is not an implied environment grant or an additional prerequisite of the two Agent operations. Administrators get all three for non-protected services. The grant UI explains that page access needs Service.View and reveal needs Environment.View, without automatically creating dependent grants. This avoids silently broadening existing permissions.

The Agent verifies its existing socket peer and opaque session, re-resolves current identity/groups, canonicalizes the service, checks every alias and protected target, then evaluates the exact permission before the reader runs. Invoke the reader with the canonical ID and reject mismatched returned identity. Retain the reader's independent target checks and final consistency validation. Recheck session/authorization and protection before releasing a computed result so a long read does not use an already-observed revocation. No authorization transaction can promise that a later external account change never races publication.

A fresh complete read for each reveal avoids retaining secrets between requests and naturally picks up changes since the list was displayed. An absent name returns absence; an empty value remains successful. The UI updates the revealed row's source label from that same read. Ordinary InspectService remains unchanged rather than silently acquiring secrets for all callers.

### Secret ownership and transport

Keep EnvironmentValues non-serializable and do not make its generic Reveal method public. Give the Agent narrowly scoped internal assembly access to borrow the selected span after authorization, copy only that value into a disposable single-value IPC payload, then dispose the complete read immediately. Metadata-only responses dispose all values after projecting value-free fields.

Use a dedicated secret-bearing result class with redacted ToString/debugger behavior and blocked ordinary serialization; avoid a positional record that prints its value. Only the operation-specific IPC writer and HTTP reveal writer can encode the one value. Clear owned byte/character buffers in finally blocks, including oversized-result rejection, cancellation and write failures. Apply this lifecycle to the result writer and reader, not only request buffers. Web disposes the received payload after HTTP writing. Minimize string copies; .NET/JSON and browser-managed memory cannot promise erasure of all transient copies.

Retain 16 KiB request and 1 MiB result framing. Validate variable names with the existing ASCII identifier grammar, with a new 4096-character request maximum inside the frame budget. Longer discovered names remain visible but are not revealable through this bounded operation; present a sanitized size limitation. Preflight serialized result size before framing and return Unavailable on overflow, with no truncation or partial result. Metadata may exceed the frame cap even within reader limits; that remains a complete-read failure, not pagination. Keep the reader's five-second budget and cumulative limits unchanged.

HTTP reveal is a same-origin anti-forgery-validated POST. Use no-store responses and no body logging, telemetry or response caching. Values never enter URLs, hidden fields or browser storage. Render with textContent or equivalently encoded text, preserving multiline content. Authentication failures follow existing cookie clearing and login behavior. Unauthorized/protected/absent service targets retain indistinguishable HTTP responses; safe environment failure categories distinguish empty success from read failure without raw errors or paths.

### Browser state

Each row begins masked with a constant mask; even empty values are not indicated until revealed. Pokaż starts a request only for that row. Ukryj clears DOM content and retained state; a subsequent reveal performs another request. There is no elapsed-time timer. Refresh/navigation invalidates pending requests and discards all values. Use per-row request generations and cancellation so late responses cannot undo hide or refresh. On pagehide clear secret state; on persisted pageshow reload through the server before presenting data. A successful value already shown cannot be recalled from the user's memory or browser merely by revoking a grant; revocation applies to subsequent requests.

Metadata failure is scoped to the section so service-state inspection still works. Use explicit pending, empty, failed and unsupported states, with no stale results substituted on failure. Reveal capability hints are presentation only; every reveal is independently authorized.

### Grants and audit persistence

Extend allowlists in Agent, Infrastructure and grant-management Web forms. Preserve canonical resource and name-plus-numeric-subject matching. Store the two new operation strings independently.

Introduce schema version 2 in a transaction: rebuild the grants table with the expanded operation CHECK while preserving IDs and unique constraints; add nullable variable_name to audit; copy existing rows without adding grants; update user_version last. Validate migration from version 1 and fresh creation, including failure rollback. Preserve all existing audit fields. Extend AuditEntry with an optional bounded validated variable name and permit only the two new operation names in audit validation.

Audit decisions before publishing data. Reveal records contain requested validated variable name, verified actor, canonical service when known, outcome and correlation. Do not echo invalid raw names. Audit failure returns no value; an audit record denotes authorization/release intent, not proof that a browser received the bytes. Do not put hashes, lengths, paths or value fragments into audit or diagnostics.

### New privileged capability and threat controls

The new capability is reading protected service configuration through the existing root reader and disclosing one authorized value; root filesystem access remains exclusively in Agent/Systemd. Attacker-controlled inputs are session, correlation ID, service/alias, variable name, grant fields, frame metadata and HTTP parameters; systemd declarations and source files are untrusted inputs too.

- Confused deputy and arbitrary service targeting: current Agent-owned identity, exact canonical grants and protection checks before acquisition and before publication; Web hints carry no authority.
- Command injection: reuse typed D-Bus and safe source reader; no shell or new command execution.
- Traversal, symlinks and TOCTOU: no client path field; reuse existing safe file resolution, source rejection and optimistic snapshot checks. Never add generic file reads.
- Arbitrary writes: none in environment operations; only existing policy storage receives narrowly validated grant/audit changes.
- Secret leakage and XSS: isolated serializers, explicit disposal, no-store and text rendering, no logging of values or raw failures.
- Resource exhaustion: retain frame/read/concurrency limits; reject excess without partial publication; bound UI requests per row.

Verify each operation's permitted, denied, malformed, malicious, unauthorized-service, protected canonical/alias, identity-change, audit-failure and cancellation paths. Reuse disposable real-systemd fixtures across 249/255/257/259 on supported x64/ARM64 CI cells for complete list/reveal flow, precedence, optional/required sources, unsupported/racing sources, and unchanged files/lifecycle/reload state. Generate values privately and assert without printing them, even on test failure. No mutation integration cases are introduced.

## Risks / Trade-offs

- Deliberate disclosure increases compromised-Web access to secrets available to observed sessions -> Agent permission checks and one-value responses bound scope; they cannot distinguish a malicious Web request from a legitimate request using the same session.
- Metadata source labels lack paths and unit-line provenance -> retain accurate generic labels rather than invent provenance.
- No hiding timer keeps values visible on an unattended page -> this is the explicit user choice; manual hide and page refresh discard them.
- Re-reading can fail or observe changed configuration -> display current read failures and updated source labels rather than stale values.
- Bounded transport cannot carry every library-valid result -> return sanitized limitations and test boundary sizes.
- A visual redesign exists on another branch -> add the section to current main and reconcile only its placement if that work lands, without changing these behaviors.

## Migration Plan

Deploy matching Web and Agent builds together. Stop the old Agent before migration, back up the root-owned policy database using the established deployment procedure, apply the atomic v1-to-v2 migration and restart Agent; sessions naturally expire on restart. A migration failure leaves version 1 intact and startup fails closed. Old Agent binaries reject schema 2, so rollback requires stopping the new Agent and restoring the pre-migration database alongside matching old binaries; do not silently delete newer grants/audits to downgrade.

Promote the final disclosure boundary, permission matrix and migration/operator guidance into SECURITY.md, docs/identity-deployment.md and docs/systemd-environment-strategy.md, with navigational updates to docs/code-map.md and the operation description in ARCHITECTURE.md. Documentation changes occur during implementation, not this planning workflow.
