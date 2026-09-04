---
name: build-afk
description: Unattended Dynamic Workflow that turns ready-for-agent GitHub issues into reviewed, test-passing, draft PRs on a shared Windows Cloud VM. Invoke with /build-afk.
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
- Process at most **5** issues per batch. If more match the query, ask the user to refine or increase `--max`.
- `GITHUB_TOKEN` with `public_repo` scope must be available in the environment. If it is missing, fail fast with a clear message.
- Skip `bug`-labeled tickets unless `/diagnosing-bugs` is installed and verified in this environment.
- Skip tickets that are ambiguous, GUI-only without a usable WPF harness, or architecturally risky. Record the reason and continue.

## Input

`/build-afk` accepts one of:

1. No arguments — scan open `ready-for-agent` issues, print the proposed batch, and **stop for user confirmation**.
2. `--all` — process every open `ready-for-agent` issue, capped by `--max N`.
3. `--max N` — limit the batch size when used with `--all` or no args.
4. `#N #M ...` — explicit issue numbers.
5. `spec #N` — process all open child issues whose body contains `## Parent` followed by `#N`.
6. `spec #N with #M #P` — process only the listed children of spec `#N`.

The default batch is limited to the next 5 `ready-for-agent` issues, ordered by lowest number first. Adjust with `--max`.

## Pre-flight

1. Capture the parent branch:
   ```powershell
   git branch --show-current
   ```
   Store as `PARENT_BRANCH`. If it is `main` or `master`, stop.

2. Verify `GITHUB_TOKEN`:
   ```powershell
   if (-not $env:GITHUB_TOKEN) { throw "GITHUB_TOKEN is missing" }
   ```

3. Resolve the issue list.
   - For no args: list open `ready-for-agent` issues, propose the batch, and stop for user confirmation. Do not start the workflow until the user confirms or re-invokes with explicit numbers/`--all`.
   - For `--all`/`--max`: fetch open `ready-for-agent` issues, limit to `max`, and continue.
   - For explicit numbers: fetch each issue body and labels.
   - For `spec #N`: fetch the spec and find children with `## Parent #N`; order children by resolving `## Blocked by` references (blockers first).

4. Filter the batch:
   - Drop `bug` issues unless `/diagnosing-bugs` is installed and works in this environment.
   - Drop issues whose body is ambiguous or whose scope is larger than one PR. Record reason.
   - If more than 5 remain, raise the limit to the user or trim to 5 and note the truncation.

5. Fetch the full body for every remaining issue. Also fetch the parent spec body when `spec #N` was requested.

## Plan and branch naming

For each issue, decide if the batch is **chained** or **independent**.

- **Chained** when the user invoked `spec #N` and the children have `## Blocked by` ordering, or when the user explicitly requested chained PRs. Each child PR targets the previous child's branch. Branch names: `build/spec-{parent}-{child1}`, `build/spec-{parent}-{child2}`, etc.
- **Independent** by default. Each PR targets `PARENT_BRANCH`. Branch names: `build/issue-{number}`.

Create a `PLAN.json` file with this schema:

```json
{
  "repo": "github.com/jrobelia/solidworks-inventree-addin",
  "parent_branch": "milestone-3",
  "chained": false,
  "issues": [
    {
      "number": 41,
      "title": "...",
      "body": "...",
      "parent_branch": "milestone-3",
      "target_branch": "milestone-3",
      "branch": "build/issue-41",
      "skip": false,
      "skip_reason": ""
    }
  ]
}
```

For chained children after the first, set `target_branch` to the previous child's `branch`. The workflow can also compute this from `chained=true` and the previous result, but explicit `target_branch` values make the plan easier to inspect.

## Run the Dynamic Workflow

1. Create a per-run directory that will not collide with other sessions:
   ```powershell
   $runDir = "C:\devin\worktrees\build-afk-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
   New-Item -ItemType Directory -Path $runDir -Force
   ```

2. Copy the skill's `workflow.py` and `CHILD_PROMPT.md` into `$runDir`.

3. Write `PLAN.json` into `$runDir`.

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

## After the workflow

Do not merge PRs. The handoff is to `/qa`:

```
/build-afk finished. Draft PRs are open. Run /qa on each branch before merging.
```

## Files in this skill

- `workflow.py` — generic Dynamic Workflow script that reads `PLAN.json` and dispatches child agents.
- `CHILD_PROMPT.md` — prompt template for each build agent.
- `REFERENCE.md` — branch naming, child output schema, PR body template, and fallback commands.
