# Design

## Context

See proposal.md for motivation. Current Web is Razor Pages with Bootstrap, a shared light layout and English literals. Services/Index and Details use five AgentService fields. LoginModel validates username (required, max 128) and password (max 1024, not required). Grants already has add/remove POST handlers and returns 403 for denied listing. Services/Details returns 404 for denied, absent and invalid targets. Program applies no-store globally to Razor Pages.

Existing tests are ServicePagesTests, SessionPagesTests and ResponseCacheTests; the latter supplies a fake IAgentIpcClient through WebApplicationFactory. CI runs locked restore, formatting, Release build/tests and Linux fixtures. See ARCHITECTURE.md, SECURITY.md, docs/code-map.md and the identity, authorization and IPC accepted specs for unchanged contracts.

## Goals / Non-Goals

**Goals:** Implement the supplied visual direction in the existing rendering stack; keep browser behavior testable and usable without JavaScript; establish one maintainable set of UI conventions.

**Non-Goals:** No new authentication state model, role discovery, privileged request, production JS framework, API endpoint, persistence schema, or change to systemd behavior. This proposal creates planning files only.

## Decisions

### 1. Keep Razor Pages with progressive enhancement

Use the existing layout, CSS and JS entry points. Extract only repeated rendering into Razor partials and small Web-only presentation helpers: brand, status badge, breadcrumbs and feedback. CSS component classes cover fields/buttons/tables without a component framework. Retain Bootstrap where ancillary forms need it, but scope Obsidian styles and resolve _Layout.cshtml.css conflicts explicitly.

Alternative: React/Vite would replace navigation and introduce a separate application without a product requirement. A CSS-only repaint would not deliver filters, accessible states or password visibility.

### 2. Use repository-contained design inputs

references/ contains the four refined images, the original Polish brief and tokens.css. These are planning references, not production screenshots/assets. Precedence: this change's specs, explicit decisions here, reference tokens/text, then raster appearance. Do not copy the images' inconsistent logos, oversized headings, gradient UI surfaces or obsolete provisional notes.

Use the exact palette and spacing in references/tokens.css: canvas #111820, surface #1B2632, accent #79E2C0, text #F3F6FA; use #65788C control boundaries rather than decorative low-contrast separators. One shared two-ear mint logo, line icons, Space Grotesk headings, system-ui body and JetBrains Mono identifiers. Bundle licensed WOFF2 fonts with Polish coverage locally, font-display swap, and keep licenses. Generate a separate text-free architectural login illustration from the reference with imagegen; render text and the logo independently. No full-screen raster used as UI.

Desktop sidebar 232px, breadcrumb strip 64px, main padding 32px, max content width 1440px; below 1024px use an in-flow expandable navigation, and below 768px 16px padding and stacked controls. Login uses 46/54 split and a 400px maximum form; hide decoration below 768px. Use 32/40px headings (28/36 mobile), 16/24 body, 14/22 table data, 44px controls (48px login), 64px minimum rows, 8px control/12px panel radii. Narrow tables scroll in a focusable named region with an overflow hint.

Alternatives: remote fonts create network/privacy dependencies; slicing a mockup into the page makes text inaccessible. A generic replacement illustration or system-only heading font would lose the selected visual identity.

### 3. Define shell presentation without inventing authority

Login and anonymous ancillary responses use the simple brand shell. Service and grant pages use the application shell after their existing request handling. Show the Uprawnienia navigation entry without claiming administrator status: current IPC supplies no role projection and the existing destination enforces 403. Do not derive admin state from a cookie or issue speculative grant-list requests to decide navigation.

Translate Serval-owned labels, error literals and validation attributes on all existing pages into Polish and set lang=pl. Do not translate identifiers, backend state values, permission identifiers or operator-provided descriptions. Use a small Web presentation text source where messages are reused, not a language switcher or runtime translation framework. Preserve parameter names, validation bounds, route behavior and grant operations. Keep existing bare denial status behavior unless a shared error body can be supplied without altering status/disclosure.

Alternative: hide grants based on a fabricated role would mislead users and create a new security contract. Styling only the three primary pages would leave broken or unreadable ancillary views.

### 4. Filters are bounded GET inputs, applied after authorization

Add optional q (max 256 UTF-16 code units) and state (max 64) to inventory and detail browsing context. Reject repeated or overlong parameters with a generic HTTP 400 after the existing authentication check, without reflecting input. Empty q means all text; empty state means all states. Compare q with Id and Description using ordinal case-insensitive substring matching, and state with ActiveState using ordinal exact equality. Keep the original authorized order and total; no new sorting or pagination.

Use a GET form and visible Filtruj submit action as the no-script path. JS hides that fallback action only after enhancement initializes and provides immediate filtering of the authorized server-rendered records, result announcements and URL updates via replaceState. Render the whole authorized inventory for enhancement, with initial filtering represented consistently; never render unauthorized data. A GET still calls ListServices before filtering and recomputes all results.

Pass bounded q/state through local detail links and the fixed /Services return route. Refresh navigates to the same local route and filter parameters. No arbitrary returnUrl, sessionStorage, localStorage or service-data cache. Unknown but bounded state values remain selected and yield zero matches when absent. Normalize matching identically in JS and .NET, including case-folding tests; if exact parity is not possible for a string, submit the GET fallback rather than return divergent results.

Alternative: saving full records in browser storage risks stale data across sessions. Adding filter parameters to IPC unnecessarily changes a privileged contract. An unbounded arbitrary return URL invites unsafe navigation.

### 5. Preserve browser session and form protections

Keep existing POST Razor forms and anti-forgery tokens for login, logout and grants. Password visibility is a non-submit button with aria-pressed. Disable only the login submit action during an actual valid submit, leaving credential fields enabled. Reset pending state on pageshow, and move focus to the error summary on returned validation failures.

Never echo password model state: clear the bound value AND its attempted ModelState value on every returned login page, including pre-Agent validation failure. Preserve optional empty-password behavior; PAM remains the decision-maker. All authentication failures use the existing generic category in Polish, not separate native/PAM diagnostics.

Keep no-store headers. For enhanced authenticated pages, pageshow with persisted=true triggers a fresh server reload; pending and restored forms reset correctly. No offline view, persistent login or service worker is introduced.

### 6. Presentation security analysis

Changed inputs are q/state and existing form fields, service query and returned metadata. Treat all as untrusted. Razor encodes text/attributes, tag helpers encode route values, JS reads textContent/data attributes and never constructs innerHTML from metadata. Input limits prevent unbounded filter reflection. Links always target fixed local routes.

Required privileged capability: none new. ListServices, InspectService, Login, Logout and grant operations keep their existing typed contracts and Agent-side trust basis. Authorization, protected aliases, audit metadata, identity and failure behavior remain defined by the accepted specs and SECURITY.md. Presentation never grants authority.

No new filesystem paths, shell/process invocation or unit targeting is derived from filters; path traversal, symlink, TOCTOU and privileged write controls remain outside this presentation change. Existing service parameters still reach the same Agent validation. The frontend must not weaken protection to show prettier error responses or load data for unauthenticated screens.

### 7. Verification and permanent documentation

Extend WebApplicationFactory rendered-HTTP tests for markup, encoded hostile metadata, blank/unknown states, bounds, no-store, authentication redirects, indistinguishable detail 404, grant denial, anti-forgery and password non-reflection. Keep existing PageModel tests as regression coverage.

Use a test-only Microsoft.Playwright .NET dependency for repeatable browser interactions. A test-owned loopback Kestrel host with the same Web composition and a test-only fake IAgentIpcClient supplies synthetic states; never add a public production demo/auth bypass. Share composition through the test harness only as necessary. Add a browser-test job/invocation to CI with pinned browser installation matching the package and locked package references. Keep screenshots free of credentials and real host data.

Cover filters/clear/refresh/back, keyboard/password toggles, loading/retry, no-JS behavior, mobile menu, empty/error states and long/untrusted strings. Check 320, 360, 768, 1024, 1440 and reference 1536x1024; 200/400% zoom, reduced motion and screen-reader behavior. Compare rendered primary screens to each reference using view_image and record a fidelity ledger for layout, typography, palette, assets, spacing and copy. Required intentional deviations include Filtruj in the no-JS view, a consistent logo, real raw states and no invented identity.

Run locked restore, formatting, Release build/tests, browser tests and the existing Linux development smoke. Existing real-systemd CI matrix remains required; no new environment/lifecycle integration cases are introduced because those operations are unchanged. Record each gate honestly as passed/failed/unverified. Complete fresh independent review under the repository workflow.

At implementation, create docs/frontend-design.md as the canonical home for the implemented design system and reference it in docs/code-map.md. Keep behavioral requirements in accepted specs on archive; do not replicate architecture/security documentation in that guide.

## Risks / Trade-offs

- Raster drift → written tokens and the explicit dimensions above govern discrepancies; compare three real renders rather than treating a generated image as a pixel-perfect spec.
- Bootstrap and isolated layout CSS conflicts → inspect all existing pages and remove only obsolete conflicting styles; no unrelated dependency cleanup.
- Client/server filter case-folding mismatch → parity cases and server fallback; permission matching remains exact and unaffected.
- UI tests require browsers → pin the test-only dependency/browser pair and make CI execution explicit, not silently skipped.
- Polish copy can invalidate text-based smoke assertions → update only affected expected UI strings while retaining their security and runtime assertions.
- Original config.yaml uses scalar rules/guidance that this CLI warns it ignores → this plan follows their intent from the file; repairing OpenSpec configuration is separate work.

## Migration Plan

Ship frontend assets and views together through the existing Web publication. No database migration, Agent deployment, IPC change or service restart feature is required. Validate assets in the existing development container and retain normal production deployment procedures. Rollback is reverting the Web presentation change and its assets; session and policy stores are unchanged.
