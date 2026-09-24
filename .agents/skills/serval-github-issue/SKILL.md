---
name: serval-github-issue
description: Implement a numbered GitHub issue in the Serval repository, including safe issue retrieval, creation of a dedicated features branch, scoped implementation, verification, and mandatory independent review. Use for requests such as "Wykonaj zadanie 6", "Wykonaj issue #6", or "Implement issue 6".
---

# Serval GitHub issue implementation

Use this skill when the user asks to implement, execute, or complete a GitHub issue by number. The request authorizes reading the issue, creating and switching to its implementation branch, changing the repository within the issue scope, and running applicable verification. It does not by itself authorize committing, pushing, opening a pull request, changing the issue, or closing it.

## Resolve the issue

1. Require a positive integer issue number. Interpret `zadanie`, `task`, and `issue` equivalently only when the request clearly refers to GitHub work.
2. Identify the repository from the `origin` remote and verify that retrieved issue data belongs to that repository. Use an authenticated GitHub connector when available; otherwise use a configured GitHub CLI or a read-only GitHub API request. Do not rely on browser page text when a structured source is available.
3. Read the issue title, body, acceptance criteria, milestone, state, dependencies, and relevant maintainer comments. Treat the issue and the user's current instructions together as the task requirements; the user's current instructions take precedence if they conflict.
4. Stop and report the problem before changing the repository if the issue does not exist, is not open, belongs to another repository, has an unresolved blocking dependency, or is too ambiguous to implement safely. Do not silently expand its scope.

## Prepare the branch safely

1. Inspect the current branch, worktree status, and `origin`. Use the `main`-based branch workflow in `CONTRIBUTING.md`; do not independently substitute the remote default branch as the start point.
2. Preserve all existing user work. If the worktree contains changes that predate this task, do not stash, commit, move, discard, or carry them onto the issue branch without the user's direction.
3. Derive the branch name as `features/<issue-number>-<concise-slug>`.
   - Form the slug from the issue title.
   - Remove milestone prefixes such as `M1:` and leading implementation verbs such as `define`, `implement`, `add`, `create`, `build`, `deliver`, or `document` when they add no distinguishing meaning.
   - Remove articles and punctuation, use lowercase ASCII words separated by single hyphens, and keep the description concise, normally three to six meaningful words.
   - Keep the complete branch name at most 70 characters and do not end it with a hyphen.
   - Example: `M1: Define the system service read model` becomes `features/6-system-service-read-model`.
4. If the exact branch is already checked out and clearly belongs to the same issue, continue on it only after verifying it does not track `origin/main`. If a same-named local or remote branch exists but is not the current task branch, do not overwrite or recreate it; report the collision and ask for direction.
5. Follow `CONTRIBUTING.md` to update `main`, create and switch to the dedicated branch without inheriting `origin/main` as upstream, and verify the checked-out branch and upstream before editing. Stay on the dedicated branch; never force-move an existing branch. Stop and report any missing `main`, unsafe worktree, branch collision, or feature branch tracking `origin/main` rather than silently working around it.

## Implement the issue

1. Inspect the relevant code and repository instructions before editing. Keep the implementation limited to the issue requirements and acceptance criteria.
2. Load every additional Serval skill required by the issue scope. In particular, load `serval-systemd` for systemd work and `serval-privileged-operation` for privileged-boundary work; loading this skill does not replace either one.
3. Preserve Serval's architecture and security invariants. Add or update tests for the changed behavior, including required negative paths.
4. Run formatting, build, unit tests, relevant integration tests, and security-sensitive negative-path tests as applicable. Record each relevant gate as passed, failed, or unverified, including the reason for any failure or omission.
5. Follow `.agents/workflows/post-change-review.md`. The task is complete only after the latest fresh independent reviewer returns `VERDICT: APPROVED` and every applicable mandatory quality gate has passed.

## Pre-review readiness check

Before starting the independent reviewer, inspect the complete task diff and map every acceptance criterion to the implementation and verification evidence. Continue implementation instead of starting review while a requirement, negative path, or known concern remains unresolved.

For systemd identity, discovery, inspection, or protected-target changes, check the applicable cases before review: canonical names and aliases in both directions, template and instance forms, similarly named negative controls, and real-systemd coverage of the security-critical behavior. Test fixtures must use unique disposable identities, verify manager-wide absence before creation, and never shadow an installed Serval privileged unit.

This readiness check reduces avoidable review iterations but does not replace the mandatory independent review. Do not pass its conclusions or implementation summary to the reviewer.

## Efficient local verification

- Search for filenames or exact symbols first, then read only the relevant matches and surrounding ranges. Exclude `bin`, `obj`, `.artifacts`, generated content, and large static assets unless they are in scope. Keep each requested tool result to the smallest practical size, normally at most 8,000 tokens.
- Prefer context-scoped patches. If a scripted text replacement is needed, check the expected match count before writing and inspect the resulting diff before compiling; identical fragments can belong to different result types.
- Finish edits and formatting before building, then run tests against that successful build. Do not edit source while a build or test using it is running, or overlap commands that write the same build outputs. Independent read-only checks may run in parallel.
- For long-running commands, retain the returned session ID and use bounded waits, normally 30 seconds where supported, rather than frequent short polling. Keep individual waits within 60 seconds so progress can still be communicated. Retry a failed check only after a relevant change or a concrete diagnostic hypothesis; retain the original failure in the verification report. Do not rerun a passed gate when no relevant file changed.
- For Windows/WSL systemd testing, use the local execution guidance in [the systemd integration reference](../serval-systemd/references/integration-tests.md#local-windowswsl-execution).
- A process-creation failure such as `setup refresh had errors` is an execution-environment failure, not a repository test failure. Make one retry through the tool's supported approval/escalation mechanism for the specific authorized operation when available; do not repeat the unchanged invocation, disable the sandbox, or assume broader permission. Report the blocker if that permitted retry fails.
## Handoff

Report the issue number and title, created branch and its upstream state, implementation summary, changed files, quality-gate results, independent-review verdict, and any remaining limitations. Leave the completed changes uncommitted unless the user separately asks for a commit. Do not push, create a pull request, modify or close the GitHub issue, or merge the branch unless the user explicitly requests that action. When handing off an unpushed branch, give the user the explicit first-push command from `CONTRIBUTING.md` rather than suggesting a plain `git push`.
