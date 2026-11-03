# Proposal

## Why

Serval's current Bootstrap scaffold does not provide the coherent navigation, readable service states or responsive workflows of the selected Obsidian design. Implementing the prepared design will make sign-in and service inspection easier to understand while retaining the existing Razor Pages application and authorization boundary.

## What Changes

- Implement the refined Obsidian sign-in, service inventory and service detail screens, with shared dark surfaces, mint accents, typography, logo and local assets.
- Provide a responsive application shell, Polish interface copy, keyboard focus, accessible forms, status labels, empty states and failure states.
- Add name/description search, active-state filtering, result counts, explicit refresh and preservation of filters while moving between inventory and details.
- Add accessible password visibility and submission feedback without changing authentication rules.
- Apply the shared styling and Polish copy to existing grant management and ancillary pages; preserve their operations and denial responses.
- Preserve the supplied visual references in this change so implementation does not depend on files outside the repository.

Non-goals: React/SPA migration, new authentication or role APIs, new privileged operations, autostart/lifecycle controls, metrics, multi-host management, environment editing, a language selector or light theme. No changes to Agent, PAM, IPC schemas, authorization policy or systemd integration are proposed.

## Capabilities

### New Capabilities

- `web/application-shell`: Obsidian styling, navigation, Polish presentation, responsive layout and accessible shared components.
- `web/login-interface`: Accessible sign-in presentation, password visibility and safe validation/submission feedback.
- `web/service-browsing`: Read-only inventory and detail presentation, filtering, refresh and browsing state.

### Modified Capabilities

None. Existing `identity/system-account-sessions`, `authorization/system-service-access` and `ipc/agent-operations` remain authoritative and unchanged; the new capabilities define browser presentation on top of them.

## Impact

Primary changes will be confined to `src/Serval.Web/Pages`, Web presentation helpers, `wwwroot`, and `tests/Serval.Web.Tests`. Locally hosted fonts, a single logo and a separate decorative login asset are needed. Browser interaction tests may add a test-only browser dependency; no production frontend framework is required. Permanent UI conventions belong in `docs/frontend-design.md` when implemented, with a link in the code map. Existing CI and disposable Linux development smoke checks must continue to pass.

The source design is the refined Obsidian package from 23 October 2026, captured under `references/`. Requirements and decisions in this change resolve raster inconsistencies and supersede the reference package's provisional behavior notes.
