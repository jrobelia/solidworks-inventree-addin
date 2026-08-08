# Build reference

## Skills to invoke

- `/tdd` — red-green loop and seams.
- `/code-review` — two-axis (Standards / Spec) review.

## Context files

- `docs/agents/issue-tracker.md` — GitHub conventions.
- `docs/agents/coding-standards.md` — build/test commands and repo standards.
- `CONTEXT.md` / `docs/agents/domain.md` — domain vocabulary.

## Branch names

- Single ticket: `build/issue-<number>`
- Batch: `build/spec-<parent-number>-<count>` (e.g. `build/spec-44-3`)
- If the name exists, increment the trailing `-<N>` suffix until free.

## Build and test commands

Run before every commit, after any review fix, and once more before the PR:

```powershell
dotnet build "Solidworks Inventree Add-In.sln"
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj"
```

If either command fails, fix the failure before proceeding.

## Review classification

`/code-review` returns separate **Standards** and **Spec** findings. Classify each finding within its original axis; keep the two lists separate.

For each finding:

1. **Verify it against the code.** Subagent findings are opinions, not tasks. If the finding is factually wrong or contradicts the spec, skip it.
2. **Classify within its axis** as **RED / YELLOW / GREEN**.
   - **RED** — a hard spec gap (Spec) or a documented hard-standards violation (Standards). Fix it if the fix is safe and small. If the fix is too large or risky, stop and ask the user whether to open the PR with a blocking dependency, create a follow-up issue, or continue.
   - **YELLOW** — a real quality or partial-spec issue. Propose a fix; ask the user if the rework is large or if the trade-off is unclear.
   - **GREEN** — style or cosmetic. Auto-fix if trivial; otherwise add it to `### Review notes`.
3. Re-run the build/test commands after any fix.
4. If review loops aren't converging, stop and ask the user instead of looping.

## PR body

- `Closes #<ticket>` for each child ticket; `Closes #<parent>` if the batch completes the spec.
- Acceptance criteria copied from the tickets.
- Build and test commands that were run.
- Changed GUI flows and edge cases.
- `### Review notes` from `/code-review`.
- `Run /qa on this branch.`

## Diff-size guard

If the diff exceeds ~500 changed lines, split `/code-review` into per-ticket or per-module passes and synthesize the findings. If a single pass still exceeds the budget, fall back to a main-session review.
