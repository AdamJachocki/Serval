# Tasks

## 1. Visual foundation and test infrastructure

- [x] 1.1 Add local Space Grotesk and JetBrains Mono WOFF2 assets with Polish coverage and licenses, one shared logo and a separate text-free login illustration matching references; verify asset loading without third-party requests and native text rendering.
- [x] 1.2 Introduce Obsidian tokens and scoped component styles in the existing Web CSS, resolve isolated layout conflicts, and verify palette/typography/control sizes against references/tokens.css at desktop and mobile sizes.
- [x] 1.3 Add a test-only Playwright .NET browser fixture with a loopback host and fake Agent using the real Web routes/assets; pin test dependencies and matching browser setup, and verify CI executes a login-page smoke without any production test bypass.
- [x] 1.4 Create docs/frontend-design.md for implemented tokens, assets, licenses and component conventions and link it from docs/code-map.md; verify every documented asset/component exists.

## 2. Shared shell and existing ancillary pages

- [x] 2.1 Implement application/simple shell variants, sidebar, breadcrumb, skip link, current navigation and native POST logout; verify rendered-HTTP tests cover login isolation, service navigation and logout anti-forgery.
- [x] 2.2 Implement responsive in-flow navigation and named table scrolling regions; verify browser keyboard/no-JS tests at 320, 360, 768 and 1024 CSS pixels and no document-level overflow.
- [x] 2.3 Apply Polish copy and shared styles to grants, index, privacy and error views without changing grant handlers or denial statuses; verify authorized add/remove, denied listing, malicious field encoding and anti-forgery through HTTP tests.
- [x] 2.4 Document responsive and accessibility conventions in the frontend guide; verify visible focus, labels, reduced motion, contrast and current-navigation semantics in browser checks.

## 3. Login workflow

- [x] 3.1 Implement the split Obsidian login form with local decorative art, visible labels and correct autocomplete; verify desktop/mobile renders and native no-JS submission.
- [x] 3.2 Add accessible password visibility and valid-submit pending feedback with pageshow reset; verify toggling never submits, duplicate submits are prevented and credential fields remain submitted.
- [x] 3.3 Translate validation and generic rejection messages, focus the error summary and clear password value/attempted ModelState on every returned form; verify missing/overlong username, overlong password, empty-password delegation and password non-reflection tests.
- [x] 3.4 Verify HTTP and browser tests retain session cookie properties, generic rejection, existing success redirect and invalid anti-forgery rejection; document the login component states in the frontend guide.

## 4. Service browsing

- [x] 4.1 Render the five-column inventory and shared status presentation with complete encoded identifiers; verify HTTP tests for all known states, unfamiliar/missing values, missing descriptions, long names and hostile metadata.
- [x] 4.2 Implement bounded q/state GET filtering after existing authentication/authorization, with exact state comparison, text matching, stable order and counts; verify combined filters, no matches, absent selected states, repeated/overlong input and anonymous/expired-session cases.
- [x] 4.3 Add progressive client filtering with server-parity handling, clear filters and URL updates; verify browser parity, no-JS fallback and empty-state announcements without additional IPC contracts.
- [x] 4.4 Implement service details, local filter-preserving return links and refresh on both views; verify canonical alias results, fixed return routes, preserved filters and indistinguishable denied/not-found/invalid 404 responses.
- [x] 4.5 Add pending, empty and retry states and history-restoration revalidation without browser data storage; verify failure responses contain no stale records, expired sessions redirect and no-store headers remain.
- [x] 4.6 Document filtering limits, return behavior and state-label mappings in the frontend guide; verify documentation matches the tests and excludes unsupported lifecycle/autostart/metrics actions.

## 5. Integrated acceptance

- [x] 5.1 Compare real login/list/details renders to the supplied references at 1536x1024 and check 320/360/768/1024/1440 widths, 200/400 percent zoom, keyboard, screen reader and reduced motion; record a fidelity ledger, intentional deviations and fixes, and verify no unresolved material visual/accessibility mismatch remains.
- [x] 5.2 Run locked restore, format verification, Release build, full solution tests and browser CI checks; record commands/results and require all applicable gates to pass.
- [x] 5.3 Run the existing disposable Linux development-host smoke and confirm all mandatory existing CI jobs, including real-systemd coverage, remain green; update only UI-copy-dependent smoke assertions if needed and record actual results without treating Windows skips as Linux evidence.
- [x] 5.4 Trace each spec scenario to test/manual acceptance evidence, validate this change with OpenSpec strict validation and obtain a fresh independent approved review under .agents/workflows/post-change-review.md; verify all required gates and findings are resolved before marking implementation complete.
