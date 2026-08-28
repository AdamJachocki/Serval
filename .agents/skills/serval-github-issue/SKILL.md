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

1. Inspect the current branch, worktree status, `origin`, and remote default branch. Fetch `origin` so the new branch can start from the latest remote default branch.
2. Preserve all existing user work. If the worktree contains changes that predate this task, do not stash, commit, move, discard, or carry them onto the issue branch without the user's direction.
3. Derive the branch name as `features/<issue-number>-<concise-slug>`.
   - Form the slug from the issue title.
   - Remove milestone prefixes such as `M1:` and leading implementation verbs such as `define`, `implement`, `add`, `create`, `build`, `deliver`, or `document` when they add no distinguishing meaning.
   - Remove articles and punctuation, use lowercase ASCII words separated by single hyphens, and keep the description concise, normally three to six meaningful words.
   - Keep the complete branch name at most 70 characters and do not end it with a hyphen.
   - Example: `M1: Define the system service read model` becomes `features/6-system-service-read-model`.
4. If the exact branch is already checked out and clearly belongs to the same issue, continue on it. If a same-named local or remote branch exists but is not the current task branch, do not overwrite or recreate it; report the collision and ask for direction.
5. Update the local default branch, then create and switch to the implementation branch from it:
   ```text
   git switch <default-branch>
   git pull
   git switch -c <branch>
   ```
   Never force-move an existing branch.

## Implement the issue

1. Inspect the relevant code and repository instructions before editing. Keep the implementation limited to the issue requirements and acceptance criteria.
2. Load every additional Serval skill required by the issue scope. In particular, load `serval-systemd` for systemd work and `serval-privileged-operation` for privileged-boundary work; loading this skill does not replace either one.
3. Preserve Serval's architecture and security invariants. Add or update tests for the changed behavior, including required negative paths.
4. Run formatting, build, unit tests, relevant integration tests, and security-sensitive negative-path tests as applicable. Record each relevant gate as passed, failed, or unverified, including the reason for any failure or omission.
5. Follow `.agents/workflows/post-change-review.md`. The task is complete only after the latest fresh independent reviewer returns `VERDICT: APPROVED` and every applicable mandatory quality gate has passed.

## Handoff

Report the issue number and title, created branch, implementation summary, changed files, quality-gate results, independent-review verdict, and any remaining limitations. Leave the completed changes uncommitted unless the user separately asks for a commit. Do not push, create a pull request, modify or close the GitHub issue, or merge the branch unless the user explicitly requests that action.
