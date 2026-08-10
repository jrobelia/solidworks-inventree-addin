# Build reference

## Skills to invoke

- `/tdd` — red-green loop and seams.
- `/code-review` — two-axis (Standards / Spec) review.

## Context files

- `docs/agents/issue-tracker.md` — GitHub conventions and parent/child issue conventions.
- `docs/agents/coding-standards.md` — build/test commands and repo standards.
- `docs/agents/code-review-known-issues.md` — how to run `/code-review` subagents in this environment.
- `CONTEXT.md` / `docs/agents/domain.md` — domain vocabulary.

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

Run the commands from `docs/agents/coding-standards.md` before every commit, after any review fix, and once more before opening the PR. If either command fails, fix the failure before proceeding.

## Code review invocation

`/code-review` needs a fixed point and its source material up front:

1. Pre-compute `git diff <PRE_BUILD_SHA>...HEAD` and `git log <PRE_BUILD_SHA>..HEAD --oneline`.
2. Fetch the parent spec and load `docs/agents/coding-standards.md`.
3. Invoke `/code-review` with the fixed point (`PRE_BUILD_SHA`) and the diff/spec/standards context.

Run the subagents per `docs/agents/code-review-known-issues.md` — foreground is the reliable default in this Devin CLI environment; background is an option only when read-only subagents are pre-approved and the pasted diff/spec/standards context fits the budget.

If the diff is large or especially risky, consider using a more capable model for the `/code-review` subagents. The diff-size guard (`~500 changed lines`) is the primary control; model choice is a secondary lever.

## Review classification

`/code-review` returns separate **Standards** and **Spec** findings. Classify each finding within its original axis; keep the two lists separate.

For each finding:

1. **Verify it against the code.** Subagent findings are opinions, not tasks. If the finding is factually wrong or contradicts the spec, skip it.
2. **Classify within its axis** as **RED / YELLOW / GREEN**.
   - **RED** — a hard spec gap (Spec) or a documented hard-standards violation (Standards). Fix it if the fix is safe and small. If the fix is too large or risky to complete in the session, create a follow-up issue, link it as a blocking dependency on the PR, and stop without merging.
   - **YELLOW** — a real quality or partial-spec issue. Propose a fix; ask the user if the rework is large or if the trade-off is unclear.
   - **GREEN** — style or cosmetic. Auto-fix if trivial; otherwise add it to `### Review notes`.
3. Re-run the build/test commands after any fix.
4. Re-run `/code-review` (or a targeted re-review of the changed areas) to verify the finding is resolved. Limit the fix → build/test → re-review cycle to **two passes**. If the finding is still unresolved after two passes, or if review loops aren't converging, stop and ask the user.

## PR body

- `Closes #<ticket>` for each child ticket; `Part of #<parent>` to reference the parent spec without closing it.
- Acceptance criteria copied from the tickets.
- Build and test commands that were run.
- Changed GUI flows and edge cases.
- `### Review notes` from `/code-review`, including any deferred or escalated findings.
- `### Deferred and follow-up issues` — list any YELLOW findings intentionally deferred and any RED findings converted into follow-up issues with their issue numbers.
- `Run /qa on this branch.`

## Diff-size guard

If the diff exceeds ~500 changed lines, split `/code-review` into per-ticket or per-module passes and synthesize the findings. If a single pass still exceeds the budget, fall back to a main-session review.

## Examples

### Single ticket

**User:** `/build #51`

- Issue `#51` is the child ticket.
- Create `build/issue-51` from `PARENT_BRANCH`.
- Propose the public seam, run `/tdd`, run build/test, commit.
- Run `/code-review` per the invocation guide above.
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
