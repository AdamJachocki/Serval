---
name: serval-privileged-operation
description: Enforce Serval's privileged trust boundary whenever work touches Serval.Agent, Web-to-Agent IPC, Unix Domain Sockets, PAM, systemd or privileged filesystem mutations, protected services, privileged authorization, escalation, or a new root capability.
---

# Serval privileged operation

Apply this skill to design, implementation, review, and tests across this boundary:

```text
Browser -> Serval.Web (unprivileged) -> Unix Domain Socket -> Serval.Agent (privileged/root) -> systemd or protected filesystem
```

Assume `Serval.Web` is compromised. Agent-side trust decisions may not depend on Web having validated, authorized, normalized, or truthfully described a request. Never move a privileged operation into Web for implementation convenience.

## Required operation analysis

Before adding or changing an operation, record in the design or change summary:

1. Every attacker-controlled input, including identity/service/path/value fields and IPC metadata.
2. The exact privileged capability required and why it must exist.
3. The smallest operation-specific request and response contract.
4. The trustworthy basis for principal identity and authorization; do not trust a caller-supplied `IsAuthorized`, role, group list, or unrestricted actor name.
5. Agent-side validation, protected-service decision, authorization permission, secret handling, audit metadata, and failure behavior.
6. Exposure to command injection, path traversal, symlink attacks, TOCTOU, arbitrary file write, arbitrary service targeting, and confused-deputy behavior, with concrete controls for applicable threats.

For a new root capability, explicitly state the need, minimal contract, and newly introduced threats. Do not implement it until those points and its negative tests are defined.

## Boundary requirements

- Expose narrow typed operations such as a single validated service action. Never add `ExecuteCommand(string)`, `ExecuteShell(string)`, `RunAsRoot(...)`, generic process launch, generic file read/write, or equivalent escape hatches.
- Authenticate the local IPC peer and bind each request to an identity/delegation the Agent can verify. Socket locality or filesystem permissions alone do not make request fields trustworthy.
- Revalidate every parameter in `Serval.Agent`, independently of Web validation. Apply allowlisted grammar and bounds before resolving units, paths, or filesystem objects.
- Enforce resource-based authorization for the exact operation and service at the privileged side. Authentication through PAM does not replace authorization. Do not collapse permissions into fixed roles.
- Enforce protected-service policy in the Agent before reads, reveals, edits, reload-related actions, or lifecycle actions. Normal Web/admin ACLs cannot override it.
- Use fixed executables with separately supplied validated arguments or a narrower typed API. Never concatenate a shell command or use `/bin/sh -c`.
- Constrain privileged filesystem access to deterministic Serval-owned roots and known operation-specific files. Use safe resolution and atomic patterns that resist traversal, symlink substitution, and check/use races.
- Return only data required by the caller. Treat every environment value and credential as secret; keep it out of logs, SQLite, telemetry, exceptions, and audit records.
- Audit actor, operation, service/resource, affected variable name when applicable, outcome, and correlation metadata. Never audit old/new values, credentials, or command payloads containing secrets.
- Fail closed on malformed identity, malformed input, unverifiable authorization context, protected targets, policy lookup failure, or ambiguous filesystem state.

## Verification gate

Every privileged operation requires tests for: permitted path, denied path, malformed input, malicious input, unauthorized service, and protected service. Add focused cases for each applicable threat identified above and prove Web-side validation bypass does not bypass Agent enforcement.

When the operation concerns systemd, unit targeting, service lifecycle, or systemd-owned configuration, also load `serval-systemd` and satisfy its real-systemd integration matrix.
