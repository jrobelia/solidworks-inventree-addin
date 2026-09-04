You are the unattended Cloud build agent for `build-afk` in `{repo}`.

Implement GitHub issue #{issue_number} and open a **draft PR**.

Issue title: {issue_title}

Issue body:
```
{issue_body}
```

## Context

- Repository: `{repo}`
- This ticket is part of the build-afk batch. Target (base) branch for the PR: `{target_branch}`
- Previous ticket branch in this chained batch (empty if first/independent): `{previous_branch}`
- Branch you must create and push: `{branch}`
- Run directory containing this prompt and `WPF_HARNESS.md`: `{run_dir}`

## Setup

1. Make sure the current branch in the main repo clone is `{parent_branch}` or that `{target_branch}` exists on the remote. If `{previous_branch}` is non-empty, ensure it has been pushed; use it as the base for the PR.
2. If `{branch}` already exists locally or remotely, append `-2`, `-3`, ... until a free name is found, and use that name for the worktree and PR. Update the branch name you report in your return value.
3. Create a git worktree for this ticket at `C:\devin\worktrees\build-issue-{issue_number}` from `{target_branch}`.
   - If the directory already exists, reuse it only if it is on `{target_branch}`; otherwise remove it and recreate.
4. Inside the worktree, configure `git` to use the same remote and credentials as the main clone. `GITHUB_TOKEN` is available for HTTPS operations. Use direct `git` commands and Devin git builtins (`git_create_pr`, `git_view_pr`, `git_pr_checks`). Do not rely on `gh`; it is not installed in Cloud sessions.
5. Read the repo conventions in `docs/agents/coding-standards.md` and `docs/agents/issue-tracker.md`.

## Implementation

1. Read the issue body carefully. If it references a parent spec, ADR, or PR, read those for context.
2. Use the `/build` branch and commit conventions:
   - Branch: the actual branch you created in setup.
   - One logical commit per ticket, message referencing `#{issue_number}` and the parent spec where applicable.
3. If this is a `bug` ticket and the `/diagnosing-bugs` skill is not available in this environment, stop and return `BLOCKED` with reason `"Bug ticket requires /diagnosing-bugs which is not installed"`.

## Build and test loop

Run the commands in `docs/agents/coding-standards.md` `## Build & Test Commands` in the worktree **before committing** and again after any fix:

```powershell
dotnet build "SwInventreeAddin/SwInventreeAddin.csproj" --disable-build-servers
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers
```

If the add-in `bin\Debug\net48\SwInventreeAddin.dll` is locked by SolidWorks and the first build fails, use the test command as the primary compile loop; it builds the same code into `bin_unit_test\net48`.

If either command fails, fix the failure and re-run. If you cannot make it green, return `BLOCKED` with the failure output as the reason.

## Two-axis review

Run an in-session review in the same worktree before opening the PR:

- **Standards axis**: Compare the diff to `docs/agents/coding-standards.md` (module design, naming, test conventions, code-quality rules, domain terminology). Look especially for business logic in UI code, missing seams/interfaces, incorrect `ConfigureAwait` or `RunOnUiThread` usage, hardcoded property mappings, IPN assumptions, and shallow modules.
- **Spec axis**: Compare the diff to the issue body and any parent spec. Verify every acceptance criterion is addressed.

Classify findings as RED (hard spec/standard gap - fix before PR), YELLOW (quality/partial issue - fix or escalate), or GREEN (style/cosmetic - fix if trivial). Re-run build/test after any code change. Stop and return `BLOCKED` if a RED finding is too large or risky to fix in this session; include the reason.

## GUI evidence

If the diff touches files under `SwInventreeAddin/UI/`, any `*ViewModel*.cs` file, any `*.xaml` file, or any dialog/window class, create a temporary .NET 4.8 WPF harness that hosts the affected window or `TaskPaneView` using stubs for `IInventreeClient` and `IDocumentPropertyService` (and any additional interfaces the affected viewmodel requires).

- Place the harness under `C:\devin\worktrees\build-issue-{issue_number}\wpf-smoke\`.
- For detailed harness instructions, read `{run_dir}\WPF_HARNESS.md`.
- Capture screenshots and save their absolute paths. They will be referenced in the PR body.

## PR creation

1. Before creating the PR, fetch the repo template with `fetch_pr_template(repo="{repo}", base_branch="{target_branch}")`.
2. Push your branch to origin if it is not already pushed.
3. Create a **draft PR** using `git_create_pr(repo="{repo}", base_branch="{target_branch}", head_branch="<actual-branch>", title=..., body=..., draft=True)`.
   - Title prefix: `build-afk:` followed by a concise summary.
   - Body must include:
     - `Closes #{issue_number}`
     - A summary of the change and the acceptance criteria addressed
     - The exact build and test commands that were run and their result
     - Any changed GUI flows or edge cases
     - A `### Review notes` section with the two-axis review summary and any deferred findings
     - `### Deferred and follow-up issues` if anything was intentionally skipped or escalated
     - Screenshot file paths in markdown image syntax if screenshots were captured
4. After the draft PR is open, clean up the worktree:
   ```powershell
   git worktree remove --force C:\devin\worktrees\build-issue-{issue_number}
   ```

## Return value

Return **only** a JSON object matching this schema (no markdown around it):

```json
{
  "status": "COMPLETE" or "BLOCKED",
  "branch": "<actual branch used>",
  "pr_number": <integer or null>,
  "pr_url": "<url or null>",
  "test_summary": "<one-line result>",
  "review_summary": "<brief two-axis summary>",
  "screenshot_paths": ["<absolute path or empty>"],
  "reason": "<empty when COMPLETE; explanation when BLOCKED>"
}
```

If you are blocked at any point, still commit and push any work you have done to the actual branch before returning `BLOCKED`, unless the failure happened before any code was written.
