# Serval Post-Change Review Workflow

## Purpose

Every reviewable change must undergo an independent review before the task is
considered complete. The canonical reviewer definition is:

`.agents/agents/serval-code-reviewer.md`

This workflow is mandatory for the changes described below.

## When review is mandatory

Run this workflow after a coherent implementation batch changes any of the
following:

- production code or tests,
- project dependencies or references,
- build, CI/CD, packaging, or deployment configuration,
- runtime or security-sensitive configuration,
- authentication, authorization, IPC, filesystem interaction, systemd
  integration, or privileged operations,
- architecture, public contracts, or data migrations,
- `AGENTS.md`, project agent definitions, project skills, or workflows that
  affect implementation, review, architecture, or security behavior.

Purely editorial documentation changes are exempt only when they cannot change
requirements, architecture, security policy, operational behavior, or agent
behavior. If uncertain whether the exemption applies, run the review.

## Workflow

### 1. Complete and verify the implementation

Complete the requested, task-scoped change and run the relevant repository
quality gates. Fix failures introduced by the change before requesting review.

Record every relevant quality gate as passed, failed, or unverified. A failed
check has a known unsuccessful result. An unverified check was not run or did
not produce a conclusive result.

Document pre-existing, unrelated, or environment-dependent failures as known
failures with their cause. Do not expand the task merely to fix them, but do not
treat a mandatory quality gate as satisfied while it is failed or unverified.

### 2. Establish the review scope

Identify the comparison base and the complete set of task-related changes.
Unless the task requires a different base, use `HEAD` and include staged,
unstaged, and untracked files belonging to the task.

Exclude unrelated pre-existing working-tree changes. If ownership of a change
is ambiguous, state that ambiguity explicitly instead of guessing.

### 3. Start a fresh reviewer agent

Create a separate `serval-code-reviewer` agent with a completely fresh context
and load:

`.agents/agents/serval-code-reviewer.md`

The reviewer MUST NOT inherit conversation turns or context from the
implementation agent. When the orchestration mechanism supports explicit
context forking, select no inherited turns, for example `fork_turns: "none"`.

Provide only:

- the original task requirements and acceptance criteria,
- the comparison base and task-related review scope,
- access to the current repository state.

Do NOT provide:

- implementation conversation history or hidden reasoning,
- implementation plans or conclusions unless they are original requirements,
- implementation summaries, self-review, or defensive explanations,
- findings or reasoning from any previous reviewer.

The reviewer reads `AGENTS.md`, relevant project documentation, applicable
skills, and the actual repository change independently.

### 4. Perform the review

The reviewer follows `.agents/agents/serval-code-reviewer.md` and remains
read-only. If an independent reviewer cannot be started or cannot inspect the
complete scope, the review requirement is not satisfied; report the limitation
instead of declaring the task complete.

### 5. Handle `VERDICT: APPROVED`

The independent review requirement is satisfied, but the task is complete only
if the quality-gate condition in step 7 is also satisfied. In the task summary,
report all `MINOR` findings and every relevant quality gate with its status:
passed, failed, or unverified. Include the cause of each failed or unverified
check. `MINOR` findings do not have to be fixed unless the task or user requires
it.

Any implementation change made in response to a `MINOR` finding starts a new
review iteration.

### 6. Handle `VERDICT: CHANGES REQUIRED`

Return the findings to the implementation agent. Address every `BLOCKER` and
`MAJOR` finding, then rerun the relevant quality gates.

After any review-driven implementation change, start another newly created
reviewer with a clean context and repeat this workflow. Never reuse the previous
reviewer.

If the implementation agent believes a finding is based on an incorrect
assumption and makes no code change, start a new independent reviewer over the
same scope. Do not give the new reviewer the disputed finding or either agent's
reasoning. If the new review still requires changes and the implementation
agent cannot resolve the issue without ambiguous requirements or expanded
scope, stop and ask the user for direction.

### 7. Completion condition

A task requiring review is complete only when both conditions hold:

1. The latest independent reviewer returns:

```text
VERDICT: APPROVED
```

2. Every applicable mandatory quality gate defined by `AGENTS.md` or more
   specific repository instructions has passed.

If a mandatory gate is failed or unverified, do not declare the task complete.
Report the blocking result and its cause, and ask the user for direction when it
cannot be resolved within the authorized task scope. Do not invent or assume an
exception to a mandatory quality gate.

Every review iteration MUST use a newly created reviewer with a fresh context.
