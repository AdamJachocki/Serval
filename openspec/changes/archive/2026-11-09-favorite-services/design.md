# Design

## Context

See `proposal.md` for the motivation. Constraints that shape the approach:

- `Serval.Web` has no persistence and no verified user identity. It holds only an opaque session credential, and the Agent resolves it to `AgentPrincipal(Name, Uid)` on every request (`ARCHITECTURE.md`, Runtime boundaries).
- The only SQLite database is the Agent's root-only `/var/lib/serval/policy.db`, schema version 2 (grants and audit). It is owned by `Infrastructure/Policy/AgentPolicyStore.cs` behind `Application/Authorization/IAgentPolicyStore.cs`.
- `ListServices` already filters by protected policy and `Service.View` and sorts ordinally by ID. Every dispatcher operation is audited, and storage or audit failure becomes `Unavailable`.
- The IPC codec in `Application/Ipc/AgentProtocol.cs` has explicit per-operation field lists and rejects unknown or duplicate fields.
- `docs/frontend-design.md` (Service browsing) forbids service records in browser storage and fixes the return target to `/Services` with only bounded `q` and `state`.

## Goals / Non-Goals

**Goals:**
- Favorite state lives where the user's identity is verified (Agent), with the same `Name + UID` binding as grants.
- The visibility of favorites is derived from the already-authorized inventory, so no favorite code path can reveal a service.
- Full progressive enhancement: the toggle is a plain form POST.

**Non-Goals:**
- No JSON or fetch endpoint and no client-side row movement. `site.js` only keeps filtering correct across two sections.
- No pruning job for stale favorites.

## Decisions

### D1. Store favorites in the Agent policy database (schema 3)

Add a table to the existing database through a transactional `user_version` 2 → 3 migration in `AgentPolicyStore.Initialize`, extending the existing 0/1 → 2 paths so that a fresh install also lands on 3:

```sql
CREATE TABLE favorites (
    subject_name TEXT NOT NULL,
    subject_uid  INTEGER NOT NULL,
    service      TEXT NOT NULL,
    PRIMARY KEY (subject_name, subject_uid, service)
);
```

Persistence is exposed through a separate narrow Application contract (`IAgentFavoriteStore`: `List(name, uid)`, `TryAdd(name, uid, service, limit, audit)`, `Remove(name, uid, service, audit)`) implemented by the same Infrastructure class. Grant logic does not see favorites and favorites logic does not see grants.

`TryAdd` performs the count check and the insert in one transaction (`INSERT OR IGNORE`, so a duplicate is not counted), and writes the audit row in the same transaction. That mirrors how `AddGrant` couples its write with the audit.

*Alternatives:* browser storage (violates `frontend-design.md`, is per-browser and is shared between Linux users on the same browser). A Web-side database (Web would need a trusted identity and a new writable path for the unprivileged user, which is a new architectural boundary). A separate Agent database file (a second migration and backup path for no isolation gain, because both files are root-only and owned by the same process).

**Promote to permanent docs:** `ARCHITECTURE.md` (Agent owns "grant storage and audit") and `docs/code-map.md` must state that the Agent database also holds per-user non-secret preferences, starting with favorites.

### D2. Favorites membership in `ListServices` as a separate ID list

`AgentResult.ListServices` gains `IReadOnlyList<string>? Favorites`. The dispatcher computes it after authorization filtering as the intersection of the stored favorites with the published canonical IDs, and the list stays ordinal-sorted. `AgentService` is unchanged, so `InspectService` and existing wire shapes for other operations do not move. The protocol writes and parses a `favorites` array only for `ListServices` success. A favorite ID that is not among the returned services is a protocol error on the Web side (defence in depth).

A store read failure throws inside the existing dispatcher `try` block, so the result is `Unavailable` with no partial list, as required by the spec.

*Alternative:* a `bool IsFavorite` on `AgentService`. It is simpler for Razor, but it changes a record shared with `InspectService` and invites a meaningless value there. A separate `ListFavorites` operation was rejected because it is an extra round trip and could return IDs outside the authorized inventory.

### D3. `AddFavorite` reuses the inspection authorization path

The order matches `InspectServiceAsync`:

1. `AuthenticateAndResolveAsync`, which gives `Unauthenticated` when it fails.
2. `TryConcreteService` (name grammar), which gives `InvalidRequest`.
3. A pre-resolution protected check, then `InspectIdentityAsync`, a protected check on the canonical identity and all aliases, and `CanView` with the current grants. Every negative outcome collapses to `NotFound`. Unlike `InspectService`, it never returns `Denied`, so add cannot distinguish "exists but forbidden" from "absent".
4. `TryAdd(..., limit: 100)` returns added, already present or limit reached. Already present counts as `Success`, and limit reached maps to a new `AgentResultCode.LimitReached`.

`LimitReached` is added to the shared enum. It is used only by `AddFavorite` and is checked only after authorization succeeds, so it cannot leak existence.

### D4. `RemoveFavorite` is authorization-free but identity-bound

After authenticating the session and validating the grammar, the Agent tries to resolve the canonical identity. If the name resolves to a non-protected service, the canonical ID is deleted. Otherwise the submitted concrete name is deleted as given. The result is always `Success`, apart from `Unauthenticated`, `InvalidRequest` and `Unavailable`. A delete can affect only rows keyed by the caller's own `(Name, Uid)`, so skipping the `Service.View` check cannot be abused, and a constant result leaves nothing to probe. The audit records the canonical service only when it resolved to a visible service, and otherwise records null, so the audit trail does not become a probe either.

### D5. Web toggle: one POST handler, PRG with a fixed status code

`Services/Index` gets `OnPostFavoriteAsync` with bound fields `service`, `action` (`add` | `remove`), `q` and `state`. Antiforgery uses the Razor Pages default, as on the Grants page. Web validates the bounds of `q` and `state` (the same limits as GET) and the length of `service`. It drops invalid filter values rather than reflecting them, calls the Agent, then answers `RedirectToPage("/Services/Index", new { q, state, favorite = <code> })`, where `<code>` is from the fixed set `limit` | `failed`, or is absent on success. GET maps `favorite` to a fixed Polish message and ignores unknown values. It is not propagated into details or refresh links, so the browsing context still carries only filters.

Each row renders a small `<form method="post" asp-page-handler="Favorite">` containing a `<button aria-pressed>` with a visually hidden accessible name, for example "Dodaj nginx.service do ulubionych". Placement: the first cell, before the identifier link.

*Alternative:* fetch with in-place row movement. It was rejected for this change to keep one code path and avoid a JSON surface. It can be added later as enhancement only.

### D6. Rendering two sections

The page model splits `Services` into `Favorites` and `Others`, preserving Agent order. With no favorites, the markup is the single existing table. With favorites, there are two tables, each with its own `<h2>` and the same columns, inside the existing table region. One shared `data-result-count` and `data-no-results` stay. `site.js` already iterates all `[data-service-row]`, so the count works unchanged. It additionally hides a section's table when that section has no visible row (`data-service-section`), so empty headings do not linger while the other section still has matches.

## Risks / Trade-offs

- [Favorites for deleted or forbidden services are invisible but count toward the limit, and the user cannot remove them from the UI.] → 100 is generous for the expected use. `RemoveFavorite` accepts unresolvable names, so a future "manage favorites" view or an admin reset can clear them without contract changes.
- [Root-owned database writes increase from admin-only grant edits to every user's clicks.] → Each add is one bounded transactional insert behind session authentication. Rows per user are capped at 100, the name length is already bounded by `TryConcreteService`, and the existing request size limits apply.
- [Schema migration on an existing installation.] → A single transaction from 2 → 3 that only creates a table. Grants and audit are untouched, and a test covers the 1 → 3 and 2 → 3 paths with existing data. Downgrade: an older Agent rejects `user_version` 3 ("Unsupported Agent policy schema"), so rollback requires restoring the pre-upgrade database backup. Document this in the release notes.
- [The PRG round trip loses focus and scroll position on long lists.] → Accepted for this change. The status message is announced through the existing live region.

## Migration Plan

1. Deploy the Agent with the schema 3 migration. It runs on Agent start in `Initialize`.
2. Deploy Web. Result parsing reads only known properties, so an older Web ignores the new `favorites` field. The reverse does not hold: a newer Web calling an older Agent gets the new operations rejected as unknown, which surfaces as the generic unavailable message. Web and Agent ship from one package and are upgraded together.
3. Rollback: stop the services, restore `/var/lib/serval/policy.db` from the backup taken before the upgrade, and reinstall the previous package.
