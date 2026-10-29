# Frontend design

Serval Web uses the Obsidian interface system for its server-rendered Razor Pages. The interface remains progressively enhanced: authentication, navigation, filters, refresh, logout and grant forms work without JavaScript.

## Foundations

The canonical tokens live in `src/Serval.Web/wwwroot/css/site.css`.

| Role | Value |
| --- | --- |
| Canvas | `#111820` |
| Sidebar | `#141e28` |
| Surface | `#1b2632` |
| Surface hover | `#223241` |
| Border | `#344657` |
| Control border | `#65788c` |
| Text | `#f3f6fa` |
| Secondary text | `#b6c2d1` |
| Accent | `#79e2c0` |
| Accent ink | `#10251e` |
| Danger | `#ff9b9b` |
| Warning | `#f2ce86` |
| Neutral | `#bcc8d6` |

Headings use local Space Grotesk at weight 600. Service identifiers and raw states use local JetBrains Mono at weights 400–500. Body copy and controls use the system UI stack. The local WOFF2 files include Latin and Latin Extended subsets so Polish copy never depends on a remote font request. Font loading uses `font-display: swap`.

Spacing follows 4, 8, 12, 16, 24, 32, 48 and 64 pixel steps. Controls are at least 44 pixels tall, login controls are 48 pixels tall, data rows are at least 64 pixels tall, controls use an 8 pixel radius and panels use a 12 pixel radius.

## Assets and licenses

All production assets are served below `src/Serval.Web/wwwroot/assets`:

- `brand-mark.svg` is the shared text-free Serval mark used by every shell.
- `login-architecture.png` is decorative, text-free generated artwork used only on the desktop login screen. The page renders its brand, tagline, labels and controls as native HTML.
- `fonts/space-grotesk-latin.woff2` and `fonts/space-grotesk-latin-ext.woff2` provide Space Grotesk.
- `fonts/jetbrains-mono-latin.woff2` and `fonts/jetbrains-mono-latin-ext.woff2` provide JetBrains Mono.
- `fonts/SPACE-GROTESK-LICENSE.txt` and `fonts/JETBRAINS-MONO-LICENSE.txt` contain the respective SIL Open Font License 1.1 terms.

The fonts were obtained as self-hosting packages from Fontsource. Production pages do not load fonts, images, styles or scripts from third-party origins.

## Shells and components

`_ObsidianLayout.cshtml` selects one of three shell variants:

- Login: a 46/54 split at desktop widths, with decorative art on the left and a form no wider than 400 pixels on the right.
- Application: a 232 pixel desktop sidebar, a 64 pixel breadcrumb strip and a content area with 32 pixel padding and a 1440 pixel maximum width.
- Simple: a compact branded shell for the home, privacy, logout and error pages.

The shared component vocabulary is implemented with semantic Razor markup and scoped CSS:

- brand and application navigation;
- page headers and breadcrumbs;
- primary, secondary, text and danger buttons;
- labeled form fields and inline validation;
- status badges with both text and a symbol;
- focusable named table regions;
- key/value detail panels;
- alerts and empty, unavailable and pending states.

Application navigation always shows Usługi and Uprawnienia. It does not claim that the current account is an administrator; the Uprawnienia destination retains its server-side Agent decision and HTTP 403 response. Logout is a native POST form with anti-forgery protection.

## Responsive and accessibility conventions

At 1024 CSS pixels and wider, the desktop sidebar is visible. Below 1024 pixels, a native `details`/`summary` Menu control reveals an in-flow navigation block. Below 768 pixels, content padding becomes 16 pixels, controls stack, detail terms appear above their values and the decorative login panel is removed.

The document itself must never scroll horizontally. Wide tables remain semantic tables and scroll only inside a focusable region labeled Lista usług or Lista uprawnień. The region includes a narrow-screen scrolling hint.

Every page provides a Przejdź do treści skip link. Keyboard focus uses a two-pixel mint outline with a three-pixel offset. Current navigation uses `aria-current="page"`, background and an inset indicator. Icon-only controls have Polish accessible names; decorative SVGs and status symbols are hidden from assistive technology. Counts and pending messages use polite live regions. Reduced-motion preference removes nonessential transition duration.

Normal text uses the text/canvas and secondary-text/canvas pairs; controls use the stronger control border. Status never relies on color alone.

## Login states

The login form always presents visible labels and `username` / `current-password` autocomplete semantics. Password is optional at the Web validation layer so the existing Agent authentication path remains authoritative for an empty password.

The enhancement-only visibility control is a non-submitting button. It changes only the password input type, keeps focus, updates its Polish accessible name and exposes `aria-pressed`. A valid submit disables only the submit action, announces Logowanie… and leaves credential fields enabled. The `pageshow` handler restores the submit action after history navigation.

Returned forms focus their error summary. Every returned form clears both the bound password and its attempted ModelState value, including input-validation failures before an Agent request. Authentication rejection remains the single generic Polish message.

## Service browsing

The inventory displays the complete authorized identifier, description, active-state presentation, raw substate and raw load state. Empty descriptions render as Brak opisu. All Agent-provided text is emitted through Razor encoding.

GET filter parameters are bounded as follows:

- `q`: at most 256 UTF-16 code units; ordinal case-insensitive substring match against identifier and description.
- `state`: at most 64 UTF-16 code units; ordinal exact match against the raw active state.
- repeated or overlong values produce a generic HTTP 400 after the normal authenticated inventory request; rejected values are not reflected.

Search and state combine with AND and retain Agent order. The initial response is authoritative and contains only Agent-authorized records. JavaScript progressively updates row visibility, the count and the current fixed local URL for ASCII queries. A changed non-ASCII query submits the GET form so .NET remains authoritative for ordinal Unicode case folding; the GET submit also remains the no-script path. No service record, session or credential is placed in browser storage.

Details and refresh links carry only bounded `q` and `state` values. The return target is always the fixed `/Services` route. Denied, missing and invalid targets retain indistinguishable HTTP 404 responses.

| Raw `ActiveState` | Label | Presentation |
| --- | --- | --- |
| `active` | Aktywna | accent |
| `inactive` | Nieaktywna | neutral |
| `failed` | Błąd | danger |
| `activating` | Aktywacja | warning |
| `deactivating` | Dezaktywacja | warning |
| `reloading` | Przeładowanie | warning |
| other nonempty value | raw value | neutral |
| absent | Nieznany stan | neutral |

These labels are presentation only. The interface intentionally provides no lifecycle, autostart, metrics, environment editing or per-service permission action.
