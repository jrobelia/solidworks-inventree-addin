# `/build-afk` reference

## Branch naming

| Batch type | Branch pattern | PR base |
| --- | --- | --- |
| Single issue | `build/issue-<number>` | `PARENT_BRANCH` |
| Independent batch | `build/issue-<number>` per ticket | `PARENT_BRANCH` |
| Chained spec | `build/spec-<parent>-<child>` per child | previous child branch, or `PARENT_BRANCH` for the first |

If the branch name `build/issue-<number>` or `build/spec-<parent>-<child>` already exists locally or remotely, the child agent should append a `-<N>` suffix (starting at `2`) until a free name is found, and use that name for both the worktree and the PR.

## PLAN.json schema

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["repo", "parent_branch", "issues"],
  "properties": {
    "repo": { "type": "string" },
    "parent_branch": { "type": "string" },
    "chained": { "type": "boolean", "default": false },
    "parent_spec": { "type": ["integer", "null"], "default": null },
    "max": { "type": ["integer", "null"], "default": null },
    "agent_mode": { "type": "string", "default": "normal" },
    "issues": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["number", "title", "body", "branch", "parent_branch", "target_branch"],
        "properties": {
          "number": { "type": "integer" },
          "title": { "type": "string" },
          "body": { "type": "string" },
          "parent_branch": { "type": "string" },
          "target_branch": { "type": "string" },
          "branch": { "type": "string" },
          "labels": { "type": "array", "items": { "type": "string" }, "default": [] },
          "skip": { "type": "boolean", "default": false },
          "skip_reason": { "type": "string" }
        }
      }
    }
  }
}
```

- `parent_spec` is optional. Set it when `chained` is `true` so the final stacking agent can name the series from the parent spec number.
- `agent_mode` is optional. It sets the `mode` argument for build, review, and fix agents. Use `swe-1.7-standard` only after your Devin environment confirms it is accepted; the default is `normal`.

## Child agent output schema

The workflow passes this JSON Schema to each `agent()` call in the build and fix phases:

```json
{
  "type": "object",
  "properties": {
    "status": { "type": "string", "enum": ["COMPLETE", "BLOCKED"] },
    "branch": { "type": "string" },
    "pr_number": { "type": ["integer", "null"] },
    "pr_url": { "type": ["string", "null"] },
    "pre_build_sha": { "type": ["string", "null"] },
    "worktree_path": { "type": ["string", "null"] },
    "test_summary": { "type": "string" },
    "review_summary": { "type": "string" },
    "screenshot_paths": {
      "type": "array",
      "items": { "type": "string" }
    },
    "reason": { "type": "string" }
  },
  "required": ["status", "branch", "reason"]
}
```

The workflow also injects the originating issue number into the returned object as `issue_number` for roll-up purposes.

`pre_build_sha` and `worktree_path` must be present when `status` is `COMPLETE`. They enable the parent to run the independent two-axis review and any fix passes.

## RESULTS.json schema

After the workflow finishes, `RESULTS.json` in the run directory contains:

```json
{
  "repo": "github.com/jrobelia/solidworks-inventree-addin",
  "parent_branch": "milestone-3",
  "results": [
    {
      "issue_number": 41,
      "status": "COMPLETE",
      "branch": "build/issue-41",
      "pr_number": 201,
      "pr_url": "https://github.com/jrobelia/solidworks-inventree-addin/pull/201",
      "pre_build_sha": "abc1234",
      "worktree_path": "C:/devin/worktrees/build-issue-41",
      "test_summary": "dotnet test passed (375 tests)",
      "review_summary": "Standards: ...; Spec: ...",
      "screenshot_paths": [],
      "reason": ""
    }
  ],
  "stack": {
    "status": "COMPLETE",
    "stack_url": "...",
    "reason": ""
  },
  "summary": "..."
}
```

The `stack` field is present only when the input was chained and a stack agent was dispatched.

## Build, format, and test commands

Use the commands from `docs/agents/coding-standards.md` `## Build & Test Commands`:

```powershell
dotnet build "SwInventreeAddin/SwInventreeAddin.csproj" --disable-build-servers
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers
```

If `SwInventreeAddin.dll` is locked by SolidWorks, the test command is the safe compile path because it writes to `bin_unit_test\net48`.

`dotnet format` runs before the first commit and again before the PR. Target only changed C# files:

```powershell
$files = (git diff --name-only --diff-filter=AM HEAD) + (git ls-files --others --exclude-standard) |
         Where-Object { $_ -like '*.cs' }
if ($files) {
    $include = foreach ($f in $files) { "--include"; $f }
    dotnet format "Solidworks Inventree Add-In.sln" @include
}
```

If `dotnet format` is not available, continue and note it in the PR.

## Two-axis review flow

The parent orchestrator, not the build agent, runs the two-axis review. After the build agent returns `COMPLETE` with a `pre_build_sha` and `worktree_path`:

1. The parent fetches the diff (`git diff <PRE_BUILD_SHA>...HEAD`) and commit list (`git log <PRE_BUILD_SHA>..HEAD --oneline`) from the worktree.
2. It dispatches two `vm_mode="shared"` reviewer agents in parallel using the profiles copied into `$runDir`:
   - **Standards** — uses `code-review-standards.md` from the run directory. It reads `docs/agents/coding-standards.md`, applies the Fowler smell baseline, and returns a `## Standards` block.
   - **Spec** — uses `code-review-spec.md` from the run directory. It reads the issue body and any referenced parent spec/ADR, and returns a `## Spec` block.
   - If a profile is missing, `workflow.py` falls back to the bundled reviewer prompts.
3. It dispatches an **adjudicator** (`lite` mode) with the rubric below. The adjudicator returns `PROCEED`, `FIX`, or `BLOCKED`.
4. If the adjudicator returns `FIX`, the parent dispatches a fix agent with the concrete instructions. After the fix, it re-runs one Standards + Spec pass and re-adjudicates.
5. The review-fix loop is capped at **two passes**. If the second pass still returns `FIX`, the parent marks the issue `BLOCKED` with the reason that the loop did not converge.

The build and fix agents do not run an in-session self-review.

## Adjudication rubric

For each finding reported by the Standards or Spec axis, the adjudicator applies one of:

- **Auto-fix** — safe and small standards/spec gaps and deterministic trivial fixes (rename a symbol, move a method, add a null check, fix a comparison, add a missing assertion, etc.). A fix agent can apply these and run build/test unattended.
- **Ignore** — false positives, lintable style nits already covered by `dotnet format`, or reviewer guesses not supported by the diff.
- **BLOCKED** — big seam/architectural risk, ambiguous or contradictory spec, missing domain knowledge, a fix too large or risky for unattended work, or the two review axes contradict each other.

The overall action is:

- `PROCEED` when no finding needs a fix.
- `FIX` when every finding is auto-fixable and the instructions are concrete enough for a fix agent.
- `BLOCKED` when any finding is too large/risky, the spec is ambiguous, the review axes contradict, or the review-fix loop does not converge after two passes.

The adjudicator returns a `findings` array: each entry has `axis`, `text`, `classification` (`auto-fix`, `ignore`, or `BLOCKED`), and `reason`.

## Model mode selection

- `agent_mode` in `PLAN.json` controls the `mode` passed to build, review, and fix `agent()` calls.
- The default is `normal`.
- Use `swe-1.7-standard` only after a smoke test in your Devin environment confirms the mode string is accepted.
- The adjudicator and stack agents run in `lite` mode because they are lightweight classification tasks.

## Stacked PRs for chained specs

After all chained child PRs are `COMPLETE` and have `pr_number`s, the parent dispatches a final `vm_mode="shared"` stack agent (`lite` mode):

- The stack agent receives the child PR numbers in bottom-to-top order.
- The bottom PR targets `PARENT_BRANCH`; each higher PR targets the previous child branch.
- The stack name is derived from `build-afk spec {parent_spec}`.
- The stack agent uses `/git_stack` if it is installed; otherwise it tries `gh` to rebase the PR bases.
- If stack creation fails, the child PRs remain as normal chained PRs and the ticket is not blocked.

Independent issues are never grouped into a stack; they remain separate draft PRs targeting `PARENT_BRANCH`.

## PR body template

```markdown
build-afk: <concise title>

Closes #{issue_number}

## Summary
<one paragraph>

## Acceptance criteria
- [ ] <criterion>

## Build, format, and test
```powershell
dotnet build "SwInventreeAddin/SwInventreeAddin.csproj" --disable-build-servers
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers
$files = (git diff --name-only --diff-filter=AM HEAD) + (git ls-files --others --exclude-standard) |
         Where-Object { $_ -like '*.cs' }
if ($files) {
    $include = foreach ($f in $files) { "--include"; $f }
    dotnet format "Solidworks Inventree Add-In.sln" @include
}
```

## GUI flows / edge cases
<if applicable>

### Review notes
<two-axis summary and any deferred findings; note any `dotnet format` skip>

### Deferred and follow-up issues
<only if something was intentionally skipped>

<screenshots if applicable>
```

## Fallback when Dynamic Workflows are unavailable

If `run_workflow` is not available or the organization has disabled Dynamic Workflows, do not manually simulate the workflow with `devin_session_create`. Instead, stop and tell the user that `/build-afk` requires Dynamic Workflows on the Windows Cloud blueprint.
