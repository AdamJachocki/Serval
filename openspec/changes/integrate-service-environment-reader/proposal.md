## Why

Issue [#40](https://github.com/AdamJachocki/Serval/issues/40) needs one complete environment read through `ISystemServiceEnvironmentReader`. The existing acquisition, decoding, parsing, and composition components are available, but acquisition currently finishes its deadline and consistency checks before parsing and composition occur.

## What Changes

- Implement the application reader in `Serval.Systemd`, preserving the M2.1 result and failure contract.
- Carry one managed-operation deadline through acquisition, decreasing-allowance parsing, composition, final consistency validation, and publication.
- Retain the resources needed to validate manager and filesystem observations after composition; discard every unpublished result on failure or cancellation.
- Preserve validation and protected-target checks on every production construction path, isolated per-read state, and separate value-free metadata and secret ownership.
- Add consumer-contract, failure/race/ownership, concurrent-read, and real-systemd flow tests.
- Document future Agent use and prior authorization obligations. No endpoint, IPC, Agent activation, Web registration, public reveal, persistence, cache, inventory change, source mutation, reload, or lifecycle action is included.

## Capabilities

### New Capabilities

- `service-environment-reading`: Complete application-facing read orchestration, operation-wide lifetime and cancellation, final validation, safe failure mapping, and publication.

### Modified Capabilities

None. The accepted `environment-file-parsing` contract remains unchanged. The implemented source-reading and composition changes remain dependencies; this change extends their orchestration without redefining grammar, precedence, supported configurations, or limits.

## Impact

Primarily `Serval.Systemd` and its tests; `Serval.Application` supplies the unchanged public contract. Internal acquisition resource lifetime needs refactoring. Update `docs/systemd-environment-strategy.md` and `docs/code-map.md` for integration and ownership. Reuse the existing systemd 249/255/257/259 harness. No new package or project is planned. This remains a sensitive internal library for the future Agent, not an authorized user operation.
