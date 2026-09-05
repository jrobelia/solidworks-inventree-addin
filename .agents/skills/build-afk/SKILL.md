---
name: build-afk
description: "Unattended Dynamic Workflow that turns ready-for-agent GitHub issues into reviewed, test-passing, draft PRs on a shared Windows Cloud VM. Invoke with /build-afk or whenever the user wants a batch build/test/review/PR loop with no manual handoff."
disable-model-invocation: true
triggers: ["user"]
---

# `/build-afk`

Run a Dynamic Workflow that implements `ready-for-agent` issues in isolated git worktrees on the same Windows Cloud VM, then opens draft PRs. Human `/qa` remains the merge gate.

`/build-afk` is a headless version of `/build`: it skips the interactive seam confirmations and review escalations, and it bills as a single Cloud session by using `vm_mode="shared"` children.

## When to use

- The user says `/build-afk`, `/build-afk #41 #52`, or `/build-afk --all`.
- The user wants to batch-process `ready-for-agent` issues unattended on the Windows Cloud blueprint.
- The user explicitly asks for an unattended Cloud build/test/review/PR loop.

## Guardrails

- The current branch must be a feature or milestone branch. If it is `main` or `master`, stop and ask the user to check out a feature/milestone branch first.
- `GITHUB_TOKEN` with `repo` scope must be available in the environment. If it is missing, fail fast with a clear message.
- Do not merge PRs. The handoff is to `/qa`.

## Input

`/build-afk` accepts one of:

1. No arguments — scan open `ready-for-agent` issues, print the proposed batch, and **stop for user confirmation**.
2. `--all` — process every open `ready-for-agent` issue. Use `--max N` to limit the batch.
3. `--max N` — limit the batch size when used with `--all` or explicit numbers.
4. `#N #M ...` — explicit issue numbers.
5. `spec #N` — process all open child issues whose body contains `## Parent` followed by `#N`.
6. `spec #N with #M #P` — process only the listed children of spec `#N`.

With no arguments, the orchestrator lists all matching `ready-for-agent` issues and stops for confirmation. Do not start the workflow until the user confirms or re-invokes with explicit numbers, `--all`, or `--max`.

## Pre-flight

1. Capture the parent branch:
   ```powershell
   git branch --show-current
   ```
   Store as `PARENT_BRANCH`. If it is `main` or `master`, stop and ask the user to check out a feature/milestone branch.

2. Verify `GITHUB_TOKEN`:
   ```powershell
   if (-not $env:GITHUB_TOKEN) { throw "GITHUB_TOKEN is missing" }
   ```

3. Resolve the issue list.
   - For no args: list open `ready-for-agent` issues and stop for user confirmation.
   - For `--all`/`--max`: fetch open `ready-for-agent` issues, limit to `max` if provided, and continue.
   - For explicit numbers: fetch each issue body and labels.
   - For `spec #N`: fetch the spec and find children with `## Parent #N`; order children by resolving `## Blocked by` references (blockers first).

4. Fetch the full body **and labels** for every remaining issue. The labels help the orchestrator decide whether a `bug` ticket is a hard-bug signal or a routine already-triaged bug.

5. Inline triage the `PLAN.json` deterministically:
   - `parent_branch` exists and is not `main`/`master`.
   - `GITHUB_TOKEN` is set.
   - `max`, if present, is a positive integer.
   - `issues` is a non-empty list and each issue has `number`, `title`, `body`, `branch`, `parent_branch`, and `target_branch`.
   - No `lite` triage child is spawned.

6. Detect hard-bug signals. If the issue title, body, or labels contain phrases like `intermittent`, `flaky`, `race`, `no deterministic repro`, `root cause unknown`, `performance regression`, etc., the build agent will attempt to build a tight, red-capable repro and run `/diagnosing-bugs` before fixing. Routine `ready-for-agent` bugs proceed through the normal TDD/review pipeline.

## Plan and branch naming

For each issue, decide if the batch is **chained** or **independent**.

- **Chained** when the user invoked `spec #N` and the children have `## Blocked by` ordering, or when the user explicitly requested chained PRs. Each child PR targets the previous child's branch. Branch names: `build/spec-{parent}-{child1}`, `build/spec-{parent}-{child2}`, etc. Add `parent_spec` to `PLAN.json` so the final stack agent can name the series.
- **Independent** by default. Each PR targets `PARENT_BRANCH`. Branch names: `build/issue-{number}`.

If the branch name `build/issue-{number}` or `build/spec-{parent}-{child}` already exists locally or remotely, the child agent should append a `-{N}` suffix (starting at `2`) until a free name is found, and use that name for both the worktree and the PR.

See `REFERENCE.md` for the full `PLAN.json`, child output, and `RESULTS.json` schemas.

## Run the Dynamic Workflow

1. Create a per-run directory that will not collide with other sessions:
   ```powershell
   $runDir = "C:\devin\worktrees\build-afk-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
   New-Item -ItemType Directory -Path $runDir -Force
   ```

2. Copy the skill's `workflow.py`, `CHILD_PROMPT.md`, `WPF_HARNESS.md`, and the two reviewer profiles `.devin/agents/code-review-standards.md` and `.devin/agents/code-review-spec.md` into `$runDir`.

3. Write `PLAN.json` into `$runDir`. See `REFERENCE.md` for the schema; set `agent_mode` if your Devin environment supports `swe-1.7-standard` or another mode, otherwise `normal` is used. For `chained` plans, copy the parent spec's full issue body into `parent_spec_body` — the workflow validates it and the final-review phase diffs the whole chain against it.

4. Call `run_workflow` with the copied script, substituting the absolute path for `$runDir`:
   ```text
   run_workflow(script_path="C:\devin\worktrees\build-afk-YYYYMMDD-HHMMSS\workflow.py", timeout_secs=<seconds>)
   ```
   Use a generous timeout for the full batch (e.g., 4 hours for 3-5 issues). The workflow processes tickets sequentially.

5. Wait for completion with `get_workflow_output(run_id=...)` and read `RESULTS.json` from `$runDir`.

6. Roll up a short final report to the user:
   - Ticket number, branch, PR number/URL, status
   - Test and review summaries
   - Any blocked tickets and reasons
   - Stack status for chained specs

## After the workflow

The handoff is to `/qa`:

```
/build-afk finished. Draft PRs are open. Run /qa on each branch before merging.
```

Do not merge PRs.

## Files in this skill

- `workflow.py` — parent orchestrator that validates the plan, dispatches build/review/fix/final-review/stack agents, and writes `RESULTS.json`.
- `CHILD_PROMPT.md` — prompt template for each build agent.
- `WPF_HARNESS.md` — step-by-step WPF smoke-harness instructions copied into each run.
- `evals/test_workflow.py` — pytest module for the workflow's helpers and dispatch gating; run `python -m pytest evals/test_workflow.py` from the skill directory. Not part of the packaged skill.
- `REFERENCE.md` — branch naming, JSON schemas, PR body template, two-axis review flow, adjudication rubric, model mode, final-review and stacked-PR guidance, and fallback behaviour.
