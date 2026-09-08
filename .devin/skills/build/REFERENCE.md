# Build reference

## Skills to invoke

- `/tdd` — red-green loop and seams.
- `/codebase-design` — shared deep-module vocabulary and design-it-twice / deepening patterns; `docs/agents/coding-standards.md` `## Module Design` is the local source of truth and points here.
- `/review` — the shared two-axis (Standards / Spec) review-and-fix loop; `/build` calls it for the per-ticket spec check and the final review.

## Context pointers

Context pointers are out-of-context material `/build` reaches only when the current branch needs them. Do not load all of them unconditionally; reach each one when its branch fires.

- `docs/agents/issue-tracker.md` — GitHub conventions and the task-graph conventions (`## Parent`, `## Blocked by`, native blocking / sub-issue links) used to find the frontier of unblocked child tickets.
- `docs/agents/coding-standards.md` — build/test commands, repo standards, and the deep-module design vocabulary in `## Module Design`.
- `CONTEXT.md` / `docs/agents/domain.md` — domain vocabulary.

## Inputs and issue hierarchy

`/build` needs a parent spec and a **task graph** of child tickets. The **frontier** is the set of unblocked child tickets — tickets with no unresolved `## Blocked by` or native blocking / sub-issue links. See `docs/agents/issue-tracker.md` for how to resolve the graph and order the frontier.

- If the user gives one issue number, treat it as a single child ticket unless the issue body declares it as a parent spec.
- If the user gives a parent spec alone, find child issues whose bodies reference the parent, resolve their blocking links, identify the frontier, and confirm the batch.
- If the user gives a parent spec and explicit child tickets, use those children, resolve their blocking links, identify the frontier, and confirm the batch.

Process tickets in frontier order.

## Branch names

- Single ticket: `build/issue-<number>`
- Batch: `build/spec-<parent>-<child>-<child>-...` (e.g. `build/spec-44-45-46-47`)
  - The first number is the parent spec; the following numbers are the child tickets.
  - This makes the branch unambiguous and reproducible.
- If the name exists, increment the trailing `-<N>` suffix until free.

## Build and test commands

Run the commands from `docs/agents/coding-standards.md` `## Build & Test Commands` before every commit, after any review fix, and once more before opening the PR. If `dotnet build "SwInventreeAddin/SwInventreeAddin.csproj"` fails on the final copy to `bin\Debug\net48\SwInventreeAddin.dll` because SolidWorks has it locked, use `dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers` as the primary loop. It compiles the same add-in code into `bin_unit_test` and never touches the locked `bin\Debug` assembly.

## Review calls

`/build` delegates review to `/review` (`.devin/skills/review/SKILL.md`); dispatch mechanics, the adjudication rubric, the re-review rule, and the two-pass cap live there. The exact call parameters are in `SKILL.md` step 6 (per-ticket spec check) and step 8 (final review). `/build` supplies the scope and acts on `REVIEW_STATUS` per `/review`'s output contract.

## PR body

- `Closes #<ticket>` for each child ticket; `Part of #<parent>` to reference the parent spec without closing it.
- Acceptance criteria copied from the tickets.
- Build and test commands that were run.
- Changed GUI flows and edge cases.
- `### Review notes` from `/review`'s `REVIEW_NOTES`, including any deferred or escalated findings.
- `### Deferred and follow-up issues` — list any YELLOW findings intentionally deferred (with the user's explicit agreement and reason) and any RED findings converted into follow-up issues with their issue numbers.
- `Run /qa on this branch. /qa will take the PR out of draft if QA passes and ask whether to merge.`

## Examples

### Single ticket

**User:** `/build #51`

- Issue `#51` is the child ticket.
- Create `build/issue-51` from `PARENT_BRANCH`.
- Propose the public seam, run `/tdd`, run build/test, commit.
- Call `/review` per `## Review calls` above.
- Push and open a draft PR to `PARENT_BRANCH`.

### Parent spec

**User:** `/build spec #44` or `/build spec #44 with #45 #46 #47`

- Issue `#44` is the parent spec.
- If the user did not list children, find child issues whose bodies have `## Parent` referencing `#44`; otherwise use the explicit children.
- Resolve `## Blocked by` links to find the frontier.
- Confirm the 3–5 child tickets with the user.
- Create `build/spec-44-45-46-47` (or whatever the actual child numbers are) from `PARENT_BRANCH`.
