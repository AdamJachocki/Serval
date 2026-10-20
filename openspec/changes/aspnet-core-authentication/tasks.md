## 1. Typed identity and Agent validation

- [x] 1.1 Add the bounded confirmed-identity DTO, ValidateSession request/result and successful-login identity to Application IPC; bump the wire version and update all typed callers/fixtures. Verify codec tests for round trips, old versions, unexpected identity fields, invalid tokens, missing/oversized identity and malformed success responses.
- [x] 1.2 Implement Agent validation using existing session and current account/group checks with sanitized auditing and no activity refresh. Verify valid, denied, forged, expired, revoked, disabled/replaced-account, lookup-failure and audit-failure cases; verify success cannot be published after audit failure.
- [x] 1.3 Return Agent-confirmed identity at login and retain revocation if login publication fails. Verify login identity originates from PAM/NSS, not submitted text, and failed login issues no usable session.

## 2. Framework authentication integration

- [x] 2.1 Register the cookie scheme, scoped validation events, authorization and middleware ordering; use the existing Data Protection setup. Verify a real middleware sign-in creates one protected Secure/HttpOnly/Strict/host-only/session-only cookie with the token in properties and no credential claim.
- [x] 2.2 Implement per-request Agent validation and a scoped authenticated-ticket accessor for IPC callers. Verify missing properties, invalid/mismatched identity, extra privilege claims, invalid/tampered/legacy tickets and Agent timeout/unavailability cannot establish trusted access; verify no positive cross-request cache.
- [x] 2.3 Configure ticket renewal separately from Agent activity as designed. Verify controlled-time tests covering 15-minute default and configured idle windows, validation-only/static traffic, accepted activity across ticket renewal, changed IP, restart and account revocation.

## 3. Login, pages and navigation

- [x] 3.1 Migrate login to SignInAsync with confirmed claims and properties; distinguish Agent unavailability from generic credential denial and clear password model/ModelState values on redisplay. Verify actual POST responses, no secret output, no ticket on failures, local return URLs and rejected external/malformed return URLs.
- [x] 3.2 Protect service/grant pages and logout using Razor authorization conventions; replace direct cookie handling with authenticated ticket access and shared invalid-session challenge handling. Verify anonymous GET/POST cannot run protected handlers, validation outage redirects to reachable login, and an operation-time invalidation also clears authentication and challenges.
- [x] 3.3 Migrate logout to acknowledged Agent revocation followed by framework sign-out, preserving unconfirmed-revocation failure behavior. Verify successful revocation rejects token replay, already-invalid sessions clear locally, and communication failure never falsely reports remote revocation.
- [x] 3.4 Render encoded verified identity and appropriate sign-in/sign-out controls from User; remove obsolete raw-cookie helpers after callers migrate. Verify anonymous/authenticated markup, no credential disclosure and preservation of existing denied-service/grant responses.
- [x] 3.5 Preserve antiforgery and no-store behavior throughout authentication changes. Verify login/logout/grant POST rejection without valid antiforgery proof, valid forms across identity transitions, and protected responses remain non-cacheable.

## 4. Integration, documentation and review

- [x] 4.1 Extend real Linux PAM/socket integration to cover confirmed login identity, ValidateSession, untrusted peer and account/restart invalidation. Run the existing disposable Agent PAM fixture and verify no credentials are printed.
- [x] 4.2 Run the existing real-systemd Agent authorization matrix with updated IPC callers, proving successful Web/session validation cannot bypass current grants, removed group membership, canonical/alias checks or protected services. Verify permitted, denied, malformed, malicious and Web-bypass paths without expanding service functionality.
- [x] 4.3 Update ARCHITECTURE.md, SECURITY.md, docs/code-map.md and docs/identity-deployment.md for ticket properties, key responsibility, IPC operation, lifetime semantics and coordinated upgrade/rollback. Verify docs match shipped behavior and do not introduce duplicate permanent guidance in task artifacts.
- [x] 4.4 Run applicable repository formatting, warning-as-error build, unit and integration gates plus strict OpenSpec validation; record each result as passed, failed or unverified with cause. Verify every delta scenario has implementation and test evidence.
- [x] 4.5 Complete .agents/workflows/post-change-review.md with a fresh independent reviewer and resolve required findings; verify the latest verdict is APPROVED and all mandatory implementation gates pass before marking implementation complete.
