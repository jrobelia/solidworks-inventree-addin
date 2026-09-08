# Build reference

## Skills to invoke

- `/tdd` — red-green loop and seams.
- `/codebase-design` — shared deep-module vocabulary and design-it-twice / deepening patterns; `docs/agents/coding-standards.md` `## Module Design` is the local source of truth and points here.
- `/code-review` — two-axis (Standards / Spec) review (fallback when the custom subagent profiles below are absent).
- `run_subagent` with profiles `code-review-standards` and `code-review-spec` — the preferred two-axis review when `.devin/agents/code-review-standards.md` and `.devin/agents/code-review-spec.md` exist.

## Context files

- `docs/agents/issue-tracker.md` — GitHub conventions and parent/child issue conventions.
- `docs/agents/coding-standards.md` — build/test commands, repo standards, and the deep-module design vocabulary in `## Module Design`.
- `docs/agents/code-review-known-issues.md` — environment notes and fallback triggers for the two-axis review.
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

## Code review invocation

The two-axis review needs a fixed point and its source material up front. Use the paths below in order: `run_subagent` is preferred, the Devin cloud child-session fallback covers environments where `run_subagent` is unavailable, and the in-session `/code-review` skill is the last resort.

1. Prepare:
   - `PRE_BUILD_SHA` — the commit the build branch started from.
   - The full body of the parent spec and any child tickets being reviewed (for the Spec subagent).
   - Confirm `Exec(git diff)`, `Exec(git log)`, and `Read(**)` are pre-approved in the active Devin config.
2. Check the diff size with `git diff <PRE_BUILD_SHA>...HEAD --stat`. If the diff exceeds ~500 changed lines, split the review into per-ticket or per-module passes.
3. Determine whether the custom profiles exist at `.devin/agents/code-review-standards.md` and `.devin/agents/code-review-spec.md`.
4. **Preferred: `run_subagent`.** If the profiles exist and `run_subagent` is available, run them in parallel in the background:

   - **Standards subagent** (`profile: code-review-standards`, `is_background=true`):
     - Pass `PRE_BUILD_SHA`.
   - **Spec subagent** (`profile: code-review-spec`, `is_background=true`):
     - Pass `PRE_BUILD_SHA` and the parent spec contents.

   The subagent profiles contain the instructions to fetch the diff and log and to read the standards file; they should not ask for pasted `DIFF:` or `COMMITS:` blocks.
5. **Fallback: Devin cloud child sessions.** If the profiles exist but `run_subagent` is unavailable (tool-denial, schema not loaded, etc.), run the two axes in parallel Devin cloud sessions via the `devin_session_create` MCP tool:

   - Create each session with `devin_session_create`. The `prompt` must contain the full text of the matching `.devin/agents/code-review-*.md` profile followed by the pre-computed context blocks. Use a `title` like `"code-review-standards"` / `"code-review-spec"`. If the tool supports batch creation, pass `sessions: [{...}, {...}]` to create both at once.

     Because child sessions may not share tool permissions, pre-compute and paste:
     - **Standards:** `prompt` = full `code-review-standards.md` profile + `DIFF:` + `COMMITS:` + `STANDARDS:` (full contents of `docs/agents/coding-standards.md`). The profile already contains the `SMELLS:` baseline.
     - **Spec:** `prompt` = full `code-review-spec.md` profile + `DIFF:` + `COMMITS:` + `SPEC:`

   - The returned `session_id` is bare; prefix it with `devin-` for all subsequent calls (e.g. `"devin-<session_id>"`).
   - Block until both sessions settle with `devin_session_gather`, passing `session_ids: ["devin-<id>", "devin-<id>"]`.
   - Read the final output or messages from each session and extract the `## Standards` or `## Spec` block.
   - If a session stalls, nudge it with `devin_session_interact` (`action: "message"`) or read its messages with `devin_session_interact` (`action: "get_messages"`) or `devin_session_events`.
   - Do **not** use `file:///C:/...` URIs; child sessions cannot resolve Windows file URIs.
6. **Fallback: `/code-review`.** If the profiles are missing, or both parallel methods fail, use the existing `/code-review` path (or `subagent_general` in the foreground per `docs/agents/code-review-known-issues.md`).

7. Parse the responses for `## Standards` and `## Spec` headings and translate each finding's severity into the RED / YELLOW / GREEN classification in `## Review classification`.

## Per-ticket spec check

For batches of more than one ticket, run a spec-axis check inside the step-6 loop so a misunderstood ticket cannot shape the tickets that build on it. It is intentionally cheap: one reviewer over one ticket's diff.

1. Before the ticket's first commit, capture `PRE_TICKET_SHA` (`git rev-parse HEAD`).
2. After the ticket's commit, dispatch the Spec axis against that range: `run_subagent` with profile `code-review-spec` (`is_background=true`), passing `PRE_TICKET_SHA` and the ticket body as `SPEC:`.
3. Verify each finding against the diff, fix genuine spec gaps, and re-run the build/test commands before starting the next ticket. Cosmetic or standards findings are not in scope here — they are the step-8 review's job.
4. Skip this check for a single-ticket build; the step-8 review covers the same diff.

## Review classification

`/code-review` returns separate **Standards** and **Spec** findings. Classify each finding within its original axis; keep the two lists separate.

For each finding:

1. **Verify it against the code.** Subagent findings are opinions, not tasks. If the finding is factually wrong or contradicts the spec, skip it.
2. **Classify within its axis** as **RED / YELLOW / GREEN**.
   - **RED** — a hard spec gap (Spec) or a documented hard-standards violation (Standards). Fix it if the fix is safe and small. If the fix is too large or risky to complete in the session, create a follow-up issue, link it as a blocking dependency on the PR, and stop without merging.
   - **YELLOW** — a real quality or partial-spec issue. Propose a fix; ask the user if the rework is large or if the trade-off is unclear.
   - **GREEN** — style or cosmetic. Auto-fix if trivial; otherwise add it to `### Review notes`.
3. **Re-run the build/test commands** after any fix.
4. **Re-run `/code-review`.** A targeted re-review of the changed areas is required after every fix that touches code or docs, even when the diff is small. This is not optional. Limit the fix → build/test → re-review cycle to **two passes**. If the finding is still unresolved after two passes, or if review loops aren't converging, stop and ask the user. If the user explicitly accepts a YELLOW finding as-is, record the decision in the PR's `### Review notes` and `### Deferred and follow-up issues` sections.

## PR body

- `Closes #<ticket>` for each child ticket; `Part of #<parent>` to reference the parent spec without closing it.
- Acceptance criteria copied from the tickets.
- Build and test commands that were run.
- Changed GUI flows and edge cases.
- `### Review notes` from `/code-review`, including any deferred or escalated findings.
- `### Deferred and follow-up issues` — list any YELLOW findings intentionally deferred (with the user's explicit agreement and reason) and any RED findings converted into follow-up issues with their issue numbers.
- `Run /qa on this branch. /qa will take the PR out of draft if QA passes and ask whether to merge.`

## Diff-size guard

If `git diff <PRE_BUILD_SHA>...HEAD --stat` shows more than ~500 changed lines, split `/code-review` into per-ticket or per-module passes and synthesize the findings. If a single pass still exceeds the budget, fall back to a main-session review.

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
