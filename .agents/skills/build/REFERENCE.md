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

Run before every commit and once more before the PR:

```powershell
dotnet build "Solidworks Inventree Add-In.sln"
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj"
```

## Review classification

`/code-review` returns **Standards** and **Spec** findings. Map each to:

- **RED / BLOCKING** — spec gap, broken functionality, hard standards violation. Fix, rerun `/code-review`, rerun build/test. Repeat up to two loops.
- **YELLOW / MAJOR** — real quality issue. Auto-fix if simple; stop and ask the user for large rework.
- **GREEN / MINOR** — style or cosmetic. Auto-fix if trivial; otherwise add to `### Review notes`.

If a RED blocker is too large to fix safely, create a follow-up issue labeled `needs-triage`, link it as a blocking dependency, and stop without merging.

## PR body

- `Closes #<ticket>` for each child ticket; `Closes #<parent>` if the batch completes the spec.
- Acceptance criteria copied from the tickets.
- Build/test commands run.
- Changed GUI flows and edge cases.
- `### Review notes` from `/code-review`.
- `Run /qa on this branch.`

## Diff-size guard

If the diff exceeds ~500 changed lines, split `/code-review` into per-ticket or per-module passes and synthesize the findings. If a single pass still exceeds the budget, fall back to a main-session review.
