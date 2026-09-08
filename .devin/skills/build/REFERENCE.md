# Build reference

## Skills to invoke

- `/tdd` — red-green loop and seams.
- `/codebase-design` — shared deep-module vocabulary and design-it-twice / deepening patterns; `docs/agents/coding-standards.md` `## Module Design` is the local source of truth and points here.
- `/review` — the shared two-axis (Standards / Spec) review-and-fix loop; `/build` calls it for the per-ticket spec check and the final review.

## Context pointers

Reach each pointer only when its branch fires.

- `docs/agents/issue-tracker.md` — needed in step 1 to resolve the task graph and frontier.
- `docs/agents/coding-standards.md` — needed before proposing any public seam (`## Module Design`) and for `## Build & Test Commands`.
- `docs/agents/pr-conventions.md` — needed when creating or updating a PR (step 5 branch naming, step 10 PR body).
- `CONTEXT.md` / `docs/agents/domain.md` — needed when the ticket or spec language needs the repo's domain terms.

## Inputs and issue hierarchy

`/build` needs a parent spec and a **task graph** of child tickets. The **frontier** is the set of unblocked child tickets. See `docs/agents/issue-tracker.md` for how to resolve the graph and order the frontier.

- If the user gives one issue number, treat it as a single child ticket unless the issue body declares it as a parent spec.
- If the user gives a parent spec alone, find child issues per `docs/agents/issue-tracker.md` and confirm the batch.
- If the user gives a parent spec and explicit child tickets, use those children and resolve the graph per `docs/agents/issue-tracker.md`.

Process tickets in frontier order.

## Branch names and PR body

See `docs/agents/pr-conventions.md` for branch naming, PR body sections, draft-PR rules, and the milestone-branch auto-close rule.

## Agent verification

Run the agent verification command from `docs/agents/coding-standards.md` `## Build & Test Commands`.

## Review calls

The exact `/review` call parameters are in `SKILL.md` step 6 (per-ticket spec check) and step 8 (final review). `/build` supplies the scope and acts on `REVIEW_STATUS` per `/review`'s output contract.

## Examples

### Single ticket

**User:** `/build #51`

- Issue `#51` is the child ticket.
- Create `build/issue-51` from `PARENT_BRANCH`.
- Propose the public seam, run `/tdd`, run the agent verification command, commit.
- Call `/review` per `## Review calls` above.
- Push and open a draft PR to `PARENT_BRANCH`.

### Parent spec

**User:** `/build spec #44` or `/build spec #44 with #45 #46 #47`

- Issue `#44` is the parent spec.
- If the user did not list children, find child issues whose bodies have `## Parent` referencing `#44`; otherwise use the explicit children.
- Resolve `## Blocked by` links to find the frontier.
- Confirm the 3–5 child tickets with the user.
- Create `build/spec-44-45-46-47` (or whatever the actual child numbers are) from `PARENT_BRANCH`.
