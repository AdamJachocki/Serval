# Proposal

## Why

Operators can inspect service state but cannot inspect its configured environment in Serval. The existing bounded environment reader can support a useful service-level view once access to metadata and deliberate disclosure of individual secrets are authorized by the Agent.

## What Changes

- Add an environment section to service details showing sorted variable names, masked values and value-free winning-source labels.
- Introduce per-service `Environment.View` and `Environment.Reveal` grants. Reveal requires both; `Service.View` alone grants neither. Current host administrators receive both for non-protected services.
- Add typed Agent operations for environment metadata and one-variable reveal, using the existing supported-declarations reader.
- Fetch a value only on explicit `Pokaż`; keep it visible until `Ukryj` or page refresh/navigation, with no time-based hiding. Never persist revealed values in browser storage.
- Audit disclosure decisions with the variable name and verified actor, never the value. Extend grant administration to support the new operations.
- Clearly label the view as supported configuration declarations, not the running process environment. Preserve safe failures and protected-service exclusions.
- Exclude editing, exports, bulk reveal, process-environment inspection, source paths, restart and daemon reload.

## Capabilities

### New Capabilities

- `web/service-environment`: Masked per-service configuration browsing and explicit individual disclosure.

### Modified Capabilities

- `authorization/system-service-access`: Separate environment grants, administrator bootstrap, and disclosure audit metadata.
- `ipc/agent-operations`: Narrow authenticated environment-list and single-variable-reveal operations.
- `service-environment-reading`: Permit the authorized Agent integration while retaining the bounded library read and preventing unrestricted reveal.

## Impact

Changes affect Application IPC and authorization contracts, Agent composition/dispatch, Infrastructure grant/audit persistence, Web service details and grant management, and focused Systemd secret-access integration. No new external dependency is planned. Existing grants retain their meaning and require no automatic upgrade. Add a backward-compatible audit schema migration for an optional variable name.

The planning baseline is updated `main`. The separate `obsidian-web-interface` work was inspected during exploration but is not required or imported by this change; adapt the environment section to whichever service-details presentation is present when implementation begins, without redesigning that page. Update canonical security, environment and identity documentation when implementation ships.
