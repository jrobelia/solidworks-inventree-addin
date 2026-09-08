# Build reference

## Skills to invoke

- `/tdd` — red-green loop and seams.
- `/codebase-design` — shared deep-module vocabulary and design-it-twice / deepening patterns; `docs/agents/coding-standards.md` `## Module Design` is the local source of truth and points here.
- `/review` — the shared two-axis (Standards / Spec) review-and-fix loop; `/build` calls it for the per-ticket spec check and the final review.

## Context files

- `docs/agents/issue-tracker.md` — GitHub conventions and parent/child issue conventions.
- `docs/agents/coding-standards.md` — build/test commands, repo standards, and the deep-module design vocabulary in `## Module Design`.
- `CONTEXT.md` / `docs/agents/domain.md` — domain vocabulary.

## Design vocabulary

Read `docs/agents/coding-standards.md` `## Module Design` before proposing any public seam. It is the repo's local source of truth for module, interface, depth, seam, adapter, leverage, and locality, and for rules like the deletion test, "two adapters = real seam", and "depth is a property of the interface"; it also points to the `/codebase-design` skill for the shared vocabulary, design-it-twice patterns, and deepening guidance. Do not duplicate those definitions here.

## Inputs and issue hierarchy

`/build` needs a parent spec and one or more child tickets. See `docs/agents/issue-tracker.md` for how to find child issues and order them by dependency (`## Parent`, `## Blocked by`, native blocking / sub-issue links).

- If the user gives one issue number, treat it as a single child ticket unless the issue body declares it as a parent spec.
- If the user gives a parent spec alone, find child issues whose bodies reference the parent and confirm the batch.
- If the user gives a parent spec and explicit child tickets, use those children and confirm the batch.

Order child tickets by dependency per `docs/agents/issue-tracker.md`.

## Starting state

`/build` must start from a clean feature branch. If `git status --short` is non-empty, stop and ask the user to commit or stash before proceeding.

## Branch names

- Single ticket: `build/issue-<number>`
- Batch: `build/spec-<parent>-<child>-<child>-...` (e.g. `build/spec-44-45-46-47`)
  - The first number is the parent spec; the following numbers are the child tickets.
  - This makes the branch unambiguous and reproducible.
- If the name exists, increment the trailing `-<N>` suffix until free.

## Build and test commands

Run the commands from `docs/agents/coding-standards.md` before every commit, after any review fix, and once more before opening the PR. If `dotnet build "SwInventreeAddin/SwInventreeAddin.csproj"` fails on the final copy to `bin\Debug\net48\SwInventreeAddin.dll` because SolidWorks has it locked, use `dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers` as the primary loop. It compiles the same add-in code into `bin_unit_test` and never touches the locked `bin\Debug` assembly.

## Review calls

`/build` delegates review to `/review` (`.devin/skills/review/SKILL.md`); dispatch mechanics, the adjudication rubric, the re-review rule, and the two-pass cap live there. `/build` supplies the scope and acts on the status.

Two calls per build:

1. **Per-ticket spec check** — inside the step-6 loop, multi-ticket batches only. After the ticket's commit, call `/review` with `REVIEW_BASE` = `PRE_TICKET_SHA` (captured before the ticket's first commit), `SPEC_SOURCE` = the ticket body, `AXES` = `spec`. Resolve spec gaps it returns before starting the next ticket. Skip for a single-ticket build — the final review covers the same diff.
2. **Final review** — step 8. Call `/review` with `REVIEW_BASE` = `PRE_BUILD_SHA`, `SPEC_SOURCE` = the parent spec body, `AXES` = `both`.

### Acting on REVIEW_STATUS

- `clean`, `resolved` — proceed.
- `deferred` — proceed; carry `REVIEW_NOTES` into the PR's `### Review notes` and `### Deferred and follow-up issues` sections.
- `escalated` — a RED finding became a follow-up issue; keep the PR in draft, note the blocker, and stop for the user.
- `capped` — the two-pass cap was hit; stop and ask the user.

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

### Parent spec with linked child issues

**User:** `/build spec #44`

- Issue `#44` is the parent spec.
- Find child issues whose bodies have `## Parent` referencing `#44`.
- Confirm the 3–5 child tickets with the user.
- Create `build/spec-44-45-46-47` (or whatever the actual child numbers are) from `PARENT_BRANCH`.
- Process tickets by resolving their `## Blocked by` sections.

### Parent spec with explicit children

**User:** `/build spec #44 with #45 #46 #47`

- Issue `#44` is the parent spec; `#45`, `#46`, `#47` are the child tickets.
- Create `build/spec-44-45-46-47` from `PARENT_BRANCH`.
- If the dependency order is unclear from the issue bodies, ask the user.
