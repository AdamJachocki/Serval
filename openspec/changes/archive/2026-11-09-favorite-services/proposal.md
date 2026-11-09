# Proposal

## Why

Users who administer a handful of services must scan or search the whole authorized inventory every time they open Serval. Letting each user pin their own services to a **Ulubione** section at the top of the list removes that repeated work without changing what they are allowed to see.

## What Changes

- Each authenticated Linux user can mark an authorized service as a favorite and remove it again, using a star toggle in that service's row on the inventory page.
- The inventory renders two sections: **Ulubione** (the user's favorites) above **Pozostałe usługi** (all other authorized services). The favorites section is omitted when it would be empty. Agent order is preserved inside each section.
- Search and state filtering apply to both sections, with one shared result count.
- Favorites are durable, per-user, non-secret application state owned by Serval.Agent in its existing SQLite database, bound to the user's Linux name and UID in the same way as grants.
- Agent gains two narrow typed operations, `AddFavorite` and `RemoveFavorite`. The `ListServices` result additionally reports which of the returned (already authorized) services are favorites of the caller.
- Favorites never widen visibility: a favorite whose service is unauthorized, protected or no longer present is not shown. Adding a favorite for such a service is rejected with the same result as for a nonexistent service.
- The number of favorites per user is bounded.

### Non-goals

- No favorite toggle on the service details page. Details stay read-only.
- No sharing of favorites between users, group-level favorites, or administrator management of other users' favorites.
- No custom ordering, renaming, tagging or grouping beyond the two sections.
- No favorites in browser storage or a new Web-side database.
- No in-place JavaScript toggle. Toggling is a server round-trip that returns to the filtered list.

## Capabilities

### New Capabilities
- `service-favorites`: per-user favorite service state. Covers ownership and binding to the Linux identity, the visibility rules, bounds and audit.

### Modified Capabilities
- `ipc/agent-operations`: adds the `AddFavorite` and `RemoveFavorite` operations and the favorites membership in the authorized inventory response.
- `web/service-browsing`: inventory presentation gains the Ulubione / Pozostałe usługi sections and the per-row toggle. Filtering and count span both sections.

## Impact

- **Serval.Agent**: dispatcher handling for the two new operations and favorites membership in `ListServices`. This is a new Agent capability, but it is not a new root capability over the OS: it touches only Agent-owned SQLite and never systemd state.
- **Serval.Application**: new IPC request and result types and the extended `ListServices` result. The policy-store contract (or a sibling store contract) gains favorites persistence.
- **Serval.Infrastructure**: `AgentPolicyStore` schema migration 2 → 3 adding the favorites table. Existing grants and audit data must survive.
- **Serval.Web**: `Services/Index` gains an antiforgery-protected POST handler, the two sections and the toggle markup. `site.js` filtering must cover both sections.
- **Docs**: `docs/frontend-design.md` (service browsing section) and `docs/code-map.md`.
- **Security**: Web is treated as untrusted. Agent revalidates identity, service name, protected policy and `Service.View` for every add. No secrets are involved.
