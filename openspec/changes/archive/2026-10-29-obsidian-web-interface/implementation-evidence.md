# Implementation evidence

## Fidelity ledger

The real pages were captured at 1536×1024 in `.artifacts/obsidian-ui/` and compared with `references/01-logowanie.png`, `02-lista-uslug.png`, and `03-szczegoly-uslugi.png` using `view_image`.

| Area | Result | Notes |
| --- | --- | --- |
| Layout | Matched | 46/54 login split, 232 px written-spec sidebar, 64 px breadcrumb strip, bounded content, five-column inventory, and two detail panels. |
| Palette and boundaries | Matched | Exact Obsidian canvas, surface, accent, text, separator, and control-boundary tokens from `tokens.css`. |
| Typography | Matched | Local Space Grotesk headings and JetBrains Mono identifiers, with system UI body text and Polish glyph coverage. |
| Brand and illustration | Matched by intent | One consistent two-ear brand mark is used everywhere. The production login illustration is a separate generated text-free asset rather than a screenshot slice. |
| Spacing and controls | Matched | Written 44/48 px controls, 64 px rows, 8/12 px radii, desktop/mobile padding, and 400 px login form govern raster differences. |
| Copy and semantics | Matched | Polish product copy is native HTML. Raw service states and identifiers remain unchanged and statuses include text plus symbols. |

Intentional deviations from provisional rasters: the written 232 px sidebar wins over the wider raster; the shared production logo replaces inconsistent raster marks; the no-JavaScript `Filtruj` action remains available; raw states are retained; no fabricated user, host, role, lifecycle, autostart, metric, or permission-detail UI was added. Initial clear-button visibility and client-filter detail-link context mismatches were found during comparison and fixed. No material mismatch remains in the three primary renders.

Responsive checks cover 320, 360, 384, 768, 1024, and 1440 CSS pixels. The 768 and 384 CSS-pixel checks represent 200% and 400% effective viewport widths from the 1536 px reference. Browser assertions cover no document overflow, the 1024 px sidebar boundary, in-flow mobile navigation, visible focus, five semantic column headers, local font loading, reduced motion, labels, live result feedback, and named table scrolling. The no-JavaScript browser context verifies native login, filtering, navigation, and menu behavior.

## Scenario trace

| Spec scenario | Evidence |
| --- | --- |
| Navigating between screens | `LoginUsesPolishSimpleShellAndOnlyLocalAssets`; three browser screenshots; same `_Brand`, tokens, and local assets. |
| Service detail navigation | `DetailsUseCanonicalResultAndOnlyFixedFilterPreservingLinks`; browser list/detail flow. |
| Grant access denied | `GrantsPreserveDenialAndEncodeAgentFields`; existing `ServicePagesTests`. |
| Grant management | `GrantMutationsRemainAntiForgedAgentOperations`; hostile-field encoding test. |
| Narrow viewport | browser widths 320/360/768/1024, no overflow, no-JS mobile menu. |
| Zoom and long content | 384 CSS-pixel reflow and `InventoryPreservesAuthorizedOrderAndCompleteLongIdentifiers`. |
| Keyboard-only use | browser focus-ring, semantic role, accessible-name, live-region, and native-control assertions. |
| Script unavailable | `NativeNoScriptNavigationAndFilteringRemainAvailable` plus rendered POST/GET tests. |
| Fresh login | `LoginUsesPolishSimpleShellAndOnlyLocalAssets`; desktop/mobile browser checks. |
| Show and hide | browser toggle assertions verify value, type, label, pressed semantics, and no submit. |
| Invalid username input | `LoginValidationIsPolishAndNeverReflectsPassword`; overlong-input theory. |
| Empty password | `EmptyPasswordStillDelegatesAndSuccessfulLoginKeepsRedirectContract`. |
| Authentication rejection | `RejectedLoginIsGenericAndInvalidAntiForgeryIsRejected`; browser returned-form flow. |
| Pending and returned form | delayed fake Agent browser test verifies one request, pending lifecycle, cleared password, and retry state. |
| Rejected input before authentication | overlong username/password theory verifies no Agent request and no reflection. |
| Missing anti-forgery token | login anti-forgery rendered test. |
| Successful sign-in | successful login test verifies redirect and secure session cookie; existing session tests verify cookie properties. |
| Available records | inventory rendered test and existing Agent-authorized service tests. |
| Untrusted metadata | hostile metadata encoding and complete long-identifier tests. |
| Combined filters | rendered combined-filter test and live browser filter parity, including Polish case folding. |
| Unavailable filter value | overlong/absent filter rendered test. |
| Bounded filter input | repeated and overlong filter tests after authorized inventory retrieval. |
| Return to filtered list | browser list-detail-return flow plus canonical fixed-route rendered test. |
| Session expires | anonymous/expired inventory test and existing stale-session tests. |
| Browser history restoration | `pageshow.persisted` reload implementation and no-store response tests. |
| Inactive and unfamiliar states | inventory state-map test covers all known, unknown, and missing active states with raw sub/load states. |
| No authorized records | rendered empty inventory behavior through the existing service page model coverage. |
| No matches | rendered absent-state and live clear-filter/count tests. |
| Refresh pending or failed | pending live region/link behavior and `InventoryFailureContainsRetryButNoStaleRows`. |
| Inspect service | detail screenshot and canonical detail rendered test. |
| Hidden or invalid target | denied/not-found/invalid theory verifies indistinguishable empty 404 responses. |
| Detail retrieval unavailable | rendered details error view retains retry/return and no service metadata. |

## Quality gates

Run on 2026-10-26:

| Gate | Result |
| --- | --- |
| `dotnet restore Serval.slnx --locked-mode` | Passed. |
| `dotnet format Serval.slnx --no-restore --verify-no-changes` | Passed; workspace-load warning only, exit 0. |
| `dotnet build Serval.slnx --configuration Release --no-restore` | Passed with 0 warnings and 0 errors. |
| `dotnet test Serval.slnx --configuration Release --no-build --no-restore` | Passed: 566 total, 518 passed, 48 platform-gated skips, 0 failed. |
| Web rendered/browser suite | Passed: 45/45, including pinned Chromium 1.64.1. |
| `openspec validate obsidian-web-interface --strict` | Passed. |
| `eng/visual-studio-compose/test-development-host.ps1` | Passed in the disposable privileged Linux container: rendered HTTP authorization, denied/anonymous/malformed/protected-alias paths, and runtime/negative systemd checks passed. Initial attempts exposed the expected UI-dependent fixture assertion (`active` moved inside semantic `<code>`); only that assertion was updated, after which the complete smoke passed. The first readiness retry still logs a transient connection refusal before success. |
| Fresh independent review | Approved with no blocking findings on the third fresh iteration after resolving all earlier major findings. |

No production systemd, Agent, IPC, PAM, authorization, or privileged filesystem behavior changed in this implementation.
