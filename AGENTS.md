# AGENTS.md — Serval

Serval is a production-grade, security-first Linux administration application.

Before making changes, read the documentation relevant to the task.

## Project documentation

- `ARCHITECTURE.md`
  System architecture, runtime boundaries, module responsibilities and code map.

- `SECURITY.md`
  Security model, trust boundaries and mandatory security invariants.

- `CONTRIBUTING.md`
  Development workflow, quality gates and repository conventions.

- `docs/`
  Long-lived technical and architectural decisions.

- `openspec/specs/`
  Current accepted product/system behavior.

- `openspec/changes/`
  Proposed and in-progress changes.

## OpenSpec

Use OpenSpec for non-trivial behavioral, architectural or security changes.
Treat accepted OpenSpec artifacts as the implementation contract.

## Project skills

Load a skill only when the current task matches its trigger.

- GitHub issue implementation:
  load `.agents/skills/serval-github-issue/SKILL.md`
  when implementing or completing a GitHub issue by number.

- systemd:
  load `.agents/skills/serval-systemd/SKILL.md`
  when the task involves systemd, units, service discovery, lifecycle, environment sources, or drop-ins.

- privileged operations:
  load `.agents/skills/serval-privileged-operation/SKILL.md`
  when the task involves Serval.Agent, IPC, PAM, protected services, privileged filesystem/systemd operations, or new root capabilities.

If a task matches more than one trigger, load all matching skills.

## Workflows

Changes covered by:

`.agents/workflows/post-change-review.md`

must complete that workflow before the task is considered finished.

## General rule

Do not duplicate permanent project knowledge in task artifacts or AGENTS.md.

Update the canonical document instead.