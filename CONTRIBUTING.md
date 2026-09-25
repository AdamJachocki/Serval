# Contributing to Serval

Thank you for helping build Serval. Contributions are expected to meet production-grade quality and security standards even while the project is under active development.

## Before you start

1. Read [`AGENTS.md`](AGENTS.md) in full.
2. Establish the task scope from the relevant issue, OpenSpec change, or direct
   user request.
3. Load and follow skills required by task before working especially across a sensitive boundary.
4. Report vulnerabilities privately according to [`SECURITY.md`](SECURITY.md); do not disclose them in a public issue.

Do not weaken a security invariant to simplify implementation. If a proposed change requires a new privileged capability, its minimal contract, trust basis, threats, failure behavior, and negative tests must be designed before implementation.

## Branch workflow

Before modifying any file:

1. Check the current branch and worktree. Preserve unrelated changes; do not
   switch branches if doing so would carry them into the new work.
2. Update the local `main` from `origin/main`:

```bash
git switch main
git pull
```
3. Create and switch to a dedicated branch from the updated local `main`,
   without inheriting `origin/main` as its upstream:

```bash
git checkout -b <branch-name>
```
4. Remain on the new branch throughout the
   implementation. If an existing feature branch tracks `origin/main`, stop and
   correct its upstream before continuing.

Do not implement directly on `main`. Do not discard or overwrite unrelated
local changes to switch branches.

## Change expectations

- Keep pull requests focused and explain the security impact.
- Preserve the dependency direction documented in [`ARCHITECTURE.md`](ARCHITECTURE.md).
- Do not add speculative abstractions, packages, or project references.
- Treat all environment-variable values and credentials as secrets. Never place them in source, test output, snapshots, logs, exceptions, telemetry, SQLite, issues, or pull requests.
- Add tests that demonstrate meaningful behavior. Security-sensitive and privileged changes require permitted and denied paths plus applicable malicious-input cases.
- Critical systemd behavior must eventually be verified on real Linux/systemd in CI; mocks cannot be the only evidence.
- Update documentation when behavior, trust boundaries, operator expectations, or security assumptions change.

## Pull requests

Complete the pull request template and identify:

- the problem and the intentionally excluded scope;
- architectural or trust-boundary effects;
- validation performed locally;
- follow-up work that should remain separate.

All CI checks must pass with warnings treated as errors. Maintainers may request additional negative-path, integration, or platform-specific testing proportional to risk.
