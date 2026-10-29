# Tasks

## 1. Permission and policy storage

- [x] 1.1 Extend operation allowlists and permission evaluation for Environment.View and Environment.Reveal, including current administrator bootstrap; verify user/group grants, reveal-without-view denial, Service.View isolation, canonical aliases and removed membership in Agent tests.
- [x] 1.2 Add optional validated variable-name audit metadata and a transactional schema-v2 migration expanding the grants CHECK and preserving IDs/history; verify fresh creation, populated v1 upgrade, repeated initialization and injected migration rollback in Infrastructure tests.
- [x] 1.3 Update docs/identity-deployment.md with the permission matrix, dependent grants, migration and database rollback procedure; verify instructions match the implemented schema and deployment behavior.

## 2. Typed environment IPC and Agent integration

- [x] 2.1 Add metadata and single-variable request/result contracts and strict codecs with the documented frame/name limits; verify duplicate/unknown fields, malformed names, boundary sizes, non-success response shapes and unchanged existing operations in Application IPC tests.
- [x] 2.2 Introduce the internal Agent-only selected-value access and disposable redacted result encoding; verify whole-result serialization remains blocked/redacted and owned buffers are cleared on success, overflow, cancellation, serialization failure and socket failure without printing test values.
- [x] 2.3 Register the existing environment reader only in Agent and implement pre-acquisition authorization plus final session, identity and protection rechecks; verify allowed reads, no reader call for denied/malicious/unauthorized/protected requests, canonical mismatch rejection and revocation during reads.
- [x] 2.4 Implement complete metadata projection and fresh one-variable disclosure with current source labels and audit-before-publication; verify empty declarations, empty/missing values, source changes between requests, audit failure, timeout, unsupported input and inconsistent snapshot without partial data.
- [x] 2.5 Extend the real-systemd Agent fixture for both operations and existing supported source combinations; verify winning values privately, alias/protected denials, optional and required files, unsafe sources and unchanged source/lifecycle/reload observations on a local Linux run, recording its platform separately from CI.
- [x] 2.6 Update SECURITY.md, ARCHITECTURE.md, docs/systemd-environment-strategy.md and docs/code-map.md to describe deliberate one-value disclosure, secret lifetime, supported-declaration scope and new operation entry points; verify no obsolete statement claims Agent can never expose authorized values.

## 3. Web view and grant administration

- [x] 3.1 Add independent metadata loading and a Polish environment section in service details with sorted names, source labels and constant masks; verify initial HTML/network metadata contains no values and Service.View-only, empty, pending and failure states behave as specified in Web tests.
- [x] 3.2 Add the anti-forgery-protected single-row reveal handler with typed Agent calls and no-store responses; verify missing/invalid CSRF, expired sessions, revoked grants, protected/absent targets, empty/multiline values, safe error mapping and no value-bearing logs or URLs.
- [x] 3.3 Implement row reveal/hide state with no hiding timer, text-only rendering, late-response suppression and refresh/history reset; verify in browser tests that only the clicked row reveals, time advancement does not hide it, Ukryj discards it, re-reveal fetches anew and refresh/back-forward restore cannot resurrect it.
- [x] 3.4 Extend grant forms and lists to select the two environment operations and explain dependencies without auto-granting; verify administrator add/remove flows and non-administrator rejection in Web and Agent tests.
- [x] 3.5 Document viewing, revealing, manual hiding, refresh behavior and access requirements in the applicable operator documentation under docs/; verify the documented UI labels and workflow against the rendered service page, including keyboard operation and narrow layouts.

## 4. Integrated verification and review

- [x] 4.1 Run the complete browser-to-Agent flow with real systemd and generated private fixture values; verify authorization bypass attempts, value-free metadata/audit, exact single-value disclosure and no configuration/lifecycle changes, without retaining screenshots or traces containing values.
- [x] 4.2 Run repository formatting, locked restore, warnings-as-errors build and applicable managed tests; record each gate as passed, failed or unverified with its reason, and verify no unrelated source changes entered the task diff.
- [ ] 4.3 Run the existing supported real-systemd CI matrix (249/255/257/259, x64/ARM64) with the new environment-operation coverage and cleanup checks; record per-cell results separately from Windows and local Linux evidence.
- [ ] 4.4 Validate the OpenSpec change and complete .agents/workflows/post-change-review.md with a fresh independent reviewer; verify the latest verdict is APPROVED and all applicable mandatory gates have passed before declaring implementation complete.
