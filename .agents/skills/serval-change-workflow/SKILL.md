---
name: serval-change-workflow
description: Prepare and carry out every Serval repository change on a dedicated feature branch, whether requirements come from a numbered GitHub issue, an OpenSpec workflow, or a direct user description. Use for any task that may write repository files and whenever any OpenSpec skill is invoked, including explore, propose, apply, update, sync, or archive.
---

# Serval change workflow

Use this skill for every task that can change the Serval repository and for every invocation of an OpenSpec skill, even when the immediate OpenSpec action is exploratory or planning-only. It establishes the task source, prepares a dedicated branch, and applies the repository's implementation and review rules.

The request authorizes creating and switching to the task branch, changing repository files within the requested scope, and running applicable verification. It does not by itself authorize committing, pushing, opening a pull request, modifying a GitHub issue, closing an issue, or merging. A user's explicit instruction to use an existing branch takes precedence over automatic branch creation.

## Establish the task contract

Classify the request before preparing the branch:

- **Numbered GitHub issue:** When the user explicitly identifies a positive issue number, including requests such as `Wykonaj zadanie 6`, `Wykonaj issue #6`, or `Implement issue 6`, resolve that issue and use it as the task contract.
- **OpenSpec change:** When any OpenSpec skill is invoked, use the selected or newly derived OpenSpec change name and its artifacts as the task contract. Do not require or create a GitHub issue unless the user explicitly identifies one.
- **Direct change:** When the user describes a repository change without a GitHub issue or OpenSpec change, treat the user's request as the task contract. Do not search for or create a matching GitHub issue merely to name or authorize the work.

When more than one source applies, combine the user's current instructions with the issue or OpenSpec artifacts. The user's current instructions take precedence if they conflict. Do not silently expand the task scope.

### Resolve an explicitly numbered issue

1. Identify the repository from the `origin` remote and verify that retrieved issue data belongs to that repository. Use an authenticated GitHub connector when available; otherwise use a configured GitHub CLI or a read-only GitHub API request. Prefer structured data over browser page text.
2. Read the issue title, body, acceptance criteria, milestone, state, dependencies, and relevant maintainer comments.
3. Stop before changing the repository if the issue does not exist, is not open, belongs to another repository, has an unresolved blocking dependency, or is too ambiguous to implement safely.

## Prepare the branch safely

Prepare the branch before editing repository files or beginning an OpenSpec workflow. If an OpenSpec skill must select among existing changes, perform only the read-only discovery needed to select the change, then prepare the branch before the workflow's first write-capable or artifact-producing action.

1. Inspect the current branch, worktree status, and `origin`. Follow the `main`-based branch workflow in `CONTRIBUTING.md`; do not independently substitute the remote default branch as the start point.
2. Preserve all existing user work. If the worktree contains changes that predate this task, do not stash, commit, move, discard, or carry them onto a new branch without the user's direction. Stop and report the conflict unless the current branch already clearly belongs to this task or the user explicitly directed work on it.
3. Derive a branch name according to the task source:
   - Numbered issue: `features/<issue-number>-<concise-slug>`, using the issue title for the slug.
   - OpenSpec change without an issue number: `features/<change-name>`, using the selected or newly derived OpenSpec change name. For an exploratory OpenSpec session that has no change yet, derive the name from the exploration topic as for a direct change.
   - Direct change: `features/<concise-slug>`, using the user's requested outcome.
4. Normalize the descriptive portion to lowercase ASCII words separated by single hyphens. Remove milestone prefixes, punctuation, articles, and leading verbs such as `define`, `implement`, `add`, `create`, `build`, `deliver`, or `document` when they add no distinguishing meaning. Keep it concise, normally three to six meaningful words. Keep the complete branch name at most 70 characters and do not end it with a hyphen.
5. If the exact branch is already checked out and clearly belongs to the same task, continue on it only after verifying it does not track `origin/main`. If a same-named local or remote branch exists but is not the current task branch, do not overwrite, recreate, or force-move it; report the collision and ask for direction.
6. Update `main`, create and switch to the dedicated branch as required by `CONTRIBUTING.md`, and verify the checked-out branch and upstream before editing. Stay on the dedicated branch. Stop and report a missing `main`, unsafe worktree, branch collision, or feature branch tracking `origin/main` rather than silently working around it.

Example names:

- Issue `6`, titled `M1: Define the system service read model`: `features/6-system-service-read-model`
- OpenSpec change `add-service-filtering`: `features/add-service-filtering`
- Direct request to add audit log retention: `features/audit-log-retention`

## Perform the change

1. Read `AGENTS.md` and the documentation relevant to the task before editing. For non-trivial behavioral, architectural, or security changes, use OpenSpec as required by `AGENTS.md`.
2. Load every additional Serval skill required by the task scope. In particular, load `serval-systemd` for systemd work and `serval-privileged-operation` for privileged-boundary work; this skill does not replace either one.
3. For an OpenSpec workflow, follow the invoked OpenSpec skill after branch preparation. Treat accepted and active artifacts as the implementation contract for the phases they govern.
4. Keep changes within the task contract and preserve Serval's architecture and security invariants. Add or update tests for changed behavior, including required negative paths.
5. Run applicable formatting, build, unit tests, integration tests, and security-sensitive negative-path tests. Record every relevant gate as passed, failed, or unverified, with the reason for any failure or omission.
6. Follow `.agents/workflows/post-change-review.md` whenever it applies. A reviewable task is complete only after the latest fresh independent reviewer returns `VERDICT: APPROVED` and every applicable mandatory quality gate has passed.
