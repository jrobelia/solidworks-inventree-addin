# Build reference

## Skills to invoke

- `/tdd` — red-green loop and seams.
- `/codebase-design` — shared deep-module vocabulary and design-it-twice / deepening patterns; `docs/agents/coding-standards.md` `## Module Design` is the local source of truth and points here.
- `/review` — the shared two-axis (Standards / Spec) review-and-fix loop; `/build` calls it for the per-ticket spec check and the final review.

## Context pointers

Reach each pointer only when its branch fires.

- `docs/agents/issue-tracker.md` — needed in step 1 to resolve the task graph and frontier.
- `docs/agents/coding-standards.md` — needed before proposing any public seam (`## Module Design`) and for `## Build & Test Commands`.
- `CONTEXT.md` / `docs/agents/domain.md` — needed when the ticket or spec language needs the repo's domain terms.

## Inputs and issue hierarchy

`/build` needs a parent spec and a **task graph** of child tickets. The **frontier** is the set of unblocked child tickets. See `docs/agents/issue-tracker.md` for how to resolve the graph and order the frontier.

- If the user gives one issue number, treat it as a single child ticket unless the issue body declares it as a parent spec.
- If the user gives a parent spec alone, find child issues per `docs/agents/issue-tracker.md` and confirm the batch.
- If the user gives a parent spec and explicit child tickets, use those children and resolve the graph per `docs/agents/issue-tracker.md`.

Process tickets in frontier order.

## Branch names

- Single ticket: `build/issue-<number>`
- Batch: `build/spec-<parent>-<child>-<child>-...` (e.g. `build/spec-44-45-46-47`)
  - The first number is the parent spec; the following numbers are the child tickets.
- If the name exists, append or increment a trailing `-<N>` suffix until free.

## Build and test commands

Run the commands from `docs/agents/coding-standards.md` `## Build & Test Commands` before every commit, after any review fix, and once more before opening the PR.

## Review calls

The exact call parameters are in `SKILL.md` step 6 (per-ticket spec check) and step 8 (final review). `/build` supplies the scope and acts on `REVIEW_STATUS` per `/review`'s output contract.

## PR body

- `Closes #<ticket>` for each child ticket (or the bug issue for a `/fix` PR); `Part of #<parent>` to reference the parent spec without closing it.
- For a `/fix` PR, add the root cause in one line and the regression test added.
- Acceptance criteria copied from the tickets.
- Build and test commands that were run.
- Changed GUI flows and edge cases.
- `### Review notes` from `/review`'s `REVIEW_NOTES`, including any deferred or escalated findings.
- `### Deferred and follow-up issues` — list any YELLOW findings intentionally deferred (with the user's explicit agreement and reason) and any RED findings converted into follow-up issues with their issue numbers.
- End with the `/qa` handoff line: `Run /qa on this branch. /qa will take the PR out of draft if QA passes and ask whether to merge.`

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
