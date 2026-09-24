# Contributing to Serval

Thank you for helping build Serval. Contributions are expected to meet production-grade quality and security standards even while the project is under active development.

## Before you start

1. Read [`AGENTS.md`](AGENTS.md) in full.
2. Check the relevant issue and keep the change within its stated scope.
3. Load and follow every project-specific skill required by `AGENTS.md` before working across a sensitive boundary.
4. Report vulnerabilities privately according to [`SECURITY.md`](SECURITY.md); do not disclose them in a public issue.

Do not weaken a security invariant to simplify implementation. If a proposed change requires a new privileged capability, its minimal contract, trust basis, threats, failure behavior, and negative tests must be designed before implementation.

## Development setup

Install a compatible .NET 10 SDK. The repository's [`global.json`](global.json) selects the baseline SDK while permitting compatible .NET 10 servicing and feature-band updates.

Restore and validate the repository with:

```bash
dotnet restore --locked-mode
dotnet format Serval.slnx --verify-no-changes --no-restore
dotnet build Serval.slnx --configuration Release --no-restore
```

When intentionally changing a NuGet dependency, run `dotnet restore` without `--locked-mode`, review every resulting lock-file change, and commit it with the project changes.

## Branch workflow

Before modifying repository files for an issue or feature:

1. Check the current branch and worktree. Preserve unrelated changes; do not
   switch branches if doing so would carry them into the new work.
2. Update the local `main` from `origin/main`:

```bash
git switch main
git pull --ff-only origin main
```
3. Create and switch to a dedicated branch from the updated local `main`,
   without inheriting `origin/main` as its upstream:

```bash
git switch --no-track -c <branch-name> main
```
4. Confirm that the new branch is checked out and does not track `origin/main`
   before editing (`git branch -vv`). Remain on the new branch throughout the
   implementation. If an existing feature branch tracks `origin/main`, stop and
   correct its upstream before continuing.

Do not implement directly on `main`. Do not discard or overwrite unrelated
local changes to switch branches. The first publication of a new branch must
explicitly target its same-named remote branch, for example
`git push -u origin <branch-name>`; subsequent plain `git push` calls can then
use that upstream. Creating a branch or implementing an issue does not itself
authorize a push.

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
