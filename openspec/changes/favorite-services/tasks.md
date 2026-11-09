# Tasks

## 1. Favorites persistence (Infrastructure, schema 3)

- [x] 1.1 Add the `IAgentFavoriteStore` contract in `Serval.Application/Authorization` (`List`, `TryAdd` returning added / already present / limit reached, `Remove`), with audit passed in as in `AddGrant`. Verify the solution builds.
- [x] 1.2 Extend `AgentPolicyStore.Initialize` to schema 3 (D1): a fresh install creates `favorites` alongside grants and audit, and the 1 → 3 and 2 → 3 paths add it in one transaction. Verify with `AgentPolicyStoreTests` cases: fresh database at version 3, existing grants and audit rows preserved across 1 → 3 and 2 → 3, and an unsupported version still rejected.
- [x] 1.3 Implement the favorite store operations on `AgentPolicyStore` with parameterized SQL, the transactional count-and-insert, the 100 limit and audit in the same transaction. Verify with tests for: per-`(name, uid)` isolation including the reused-name and reused-UID cases, idempotent add, already present at the limit succeeding, the limit rejecting without change, remove affecting only the caller, and rollback when the audit insert fails.

## 2. IPC contract (Application)

- [x] 2.1 Add `AgentRequest.AddFavorite` / `RemoveFavorite` (session, correlation ID, service), the matching results, `AgentResultCode.LimitReached`, and `Favorites` on `AgentResult.ListServices` (D2). Verify that it builds.
- [x] 2.2 Extend `AgentProtocol` request and result codecs with exact field lists for both operations, the `favorites` array on `ListServices` success only, and the bounded service length as in `InspectService`. Verify with `AgentProtocolTests` round trips plus rejection of unknown, duplicate or identity fields, over-length names and a non-string favorites entry.

## 3. Agent operations (dispatcher)

- [x] 3.1 Compute `ListServices` favorite membership as the intersection of the stored favorites with the published IDs (D2). Verify with `AgentDispatcherTests`: favorites only within the authorized and unprotected output, a revoked grant hiding the favorite and restoring it after a re-grant, and a store failure giving `Unavailable` with no list.
- [x] 3.2 Implement `AddFavorite` per D3. Verify with dispatcher tests for: permitted canonical and alias adds (canonical stored), identical `NotFound` for unauthorized, protected, alias-to-protected and nonexistent targets with nothing stored, `InvalidRequest` for malformed or template names, `Unauthenticated` for bad sessions, `LimitReached`, and audit content.
- [x] 3.3 Implement `RemoveFavorite` per D4. Verify with dispatcher tests showing identical `Success` for existing, absent, protected, unauthorized and nonexistent services, no effect on another user's rows, `InvalidRequest` and `Unauthenticated` paths, and an audit service that is null when the name does not resolve to a visible service.
- [x] 3.4 Register both operations in the dispatcher's activity recording and the `Unavailable` audit fallback. Verify with a test that a store exception yields `Unavailable` and an audit attempt for each new operation.
- [ ] 3.5 Add a real-systemd Agent authorization case (alongside `RealSystemdAgentAuthorizationTests`) showing that add and remove leave systemd state and environment unread and unchanged, and that a protected unit's alias is rejected. Verify that it passes in the Linux integration run.

## 4. Web inventory (Services/Index)

- [x] 4.1 Split the inventory into favorites and other services in `IndexModel` (Agent order kept), reject a favorite ID outside the returned list as unavailable, and map the `favorite` status query value (`limit` | `failed`) to fixed Polish messages. Verify with `ServicePagesTests` for both sections, the single list without favorites, an unknown status value being ignored, and the inventory failure showing no sections.
- [x] 4.2 Add `OnPostFavoriteAsync` per D5 (antiforgery, session handling, bounded `q` and `state` dropped when invalid, redirect only to `/Services`). Verify with tests for add and remove redirects preserving filters, a missing antiforgery token sending no Agent call, a stale session clearing the cookie and redirecting to login, `NotFound` / `InvalidRequest` / `Unavailable` giving the generic message, `LimitReached` giving the limit message, and an injected return URL being ignored.
- [x] 4.3 Render the two sections and the per-row star form with `aria-pressed` and a visually hidden accessible name in `Index.cshtml`, with styles in `site.css` that do not convey state by color alone. Verify with `RenderedWebTests`: encoded identifiers in accessible names, headings present only with favorites, and both tables keeping the existing columns.
- [x] 4.4 Update `site.js` so filtering hides a section whose rows are all hidden and keeps one shared count across both sections. Verify with a `BrowserInteractionTests` / Playwright case: filtering across sections, a section without matches hidden, the overall no-results state only when both are empty, and a no-JS star toggle round trip.

## 5. Documentation

- [x] 5.1 Update `docs/frontend-design.md` (Service browsing: sections, toggle, `favorite` status parameter not carried into the browsing context) and `docs/code-map.md` (favorites store, operations). Verify that the documents match the implemented routes and names.
- [x] 5.2 Promote the D1 decision to `ARCHITECTURE.md` (the Agent database also holds per-user non-secret preferences) and add the schema 3 rollback note where deployment is documented (`docs/identity-deployment.md`). Verify that no OpenSpec artifact duplicates that text.

## 6. Integration and review

- [ ] 6.1 Run the formatting, build, full unit test, Web browser test and real-systemd integration suites from `CONTRIBUTING.md`, and record each gate as passed, failed or unverified.
- [ ] 6.2 Complete `.agents/workflows/post-change-review.md` until the fresh independent reviewer returns `VERDICT: APPROVED`.

## Workflow follow-up

- Archive the change after review approval and merge.
