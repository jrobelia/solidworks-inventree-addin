# `/build-afk` reference

## Branch naming

| Batch type | Branch pattern | PR base |
| --- | --- | --- |
| Single issue | `build/issue-<number>` | `PARENT_BRANCH` |
| Independent batch | `build/issue-<number>` per ticket | `PARENT_BRANCH` |
| Chained spec | `build/spec-<parent>-<child>` per child | previous child's branch, or `PARENT_BRANCH` for the first |

If a branch name already exists locally or remotely, append a `-<N>` suffix until a free name is found, starting at `2`.

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
    "issues": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["number", "title", "body", "branch"],
        "properties": {
          "number": { "type": "integer" },
          "title": { "type": "string" },
          "body": { "type": "string" },
          "parent_branch": { "type": "string" },
          "target_branch": { "type": "string" },
          "branch": { "type": "string" },
          "skip": { "type": "boolean", "default": false },
          "skip_reason": { "type": "string" }
        }
      }
    }
  }
}
```

## Child agent output schema

The workflow passes this JSON Schema to each `agent()` call:

```json
{
  "type": "object",
  "properties": {
    "status": { "type": "string", "enum": ["COMPLETE", "BLOCKED"] },
    "branch": { "type": "string" },
    "pr_number": { "type": ["integer", "null"] },
    "pr_url": { "type": ["string", "null"] },
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

## RESULTS.json schema

After the workflow finishes, `RESULTS.json` in the run directory contains:

```json
{
  "repo": "github.com/jrobelia/solidworks-inventree-addin",
  "parent_branch": "milestone-3",
  "results": [
    {
      "status": "COMPLETE",
      "branch": "build/issue-41",
      "pr_number": 201,
      "pr_url": "https://github.com/jrobelia/solidworks-inventree-addin/pull/201",
      "test_summary": "dotnet test passed (375 tests)",
      "review_summary": "Standards: 1 green style note. Spec: all AC met.",
      "screenshot_paths": [],
      "reason": ""
    }
  ],
  "summary": "..."
}
```

## Build and test commands

Primary verification loop for C# changes:

```powershell
dotnet build "SwInventreeAddin/SwInventreeAddin.csproj" --disable-build-servers
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers
```

If `SwInventreeAddin.dll` is locked by SolidWorks, the test command is the safe compile path because it writes to `bin_unit_test\net48`.

## PR body template

```markdown
build-afk: <concise title>

Closes #{issue_number}

## Summary
<one paragraph>

## Acceptance criteria
- [ ] <criterion>

## Build and test
```powershell
dotnet build "SwInventreeAddin/SwInventreeAddin.csproj" --disable-build-servers
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers
```

## GUI flows / edge cases
<if applicable>

### Review notes
<two-axis summary and any deferred findings>

### Deferred and follow-up issues
<only if something was intentionally skipped>

<screenshots if applicable>
```

## Fallback when Dynamic Workflows are unavailable

If `run_workflow` is not available or the organization has disabled Dynamic Workflows, do not manually simulate the workflow with `devin_session_create`. Instead, stop and tell the user that `/build-afk` requires Dynamic Workflows on the Windows Cloud blueprint.
