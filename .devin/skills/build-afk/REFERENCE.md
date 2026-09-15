# `/build-afk` reference

## Inputs and issue hierarchy

The input is a parent spec. Find child issues whose bodies contain `## Parent` referencing it, per `docs/agents/issue-tracker.md` `## Parent and child issues`. Resolve each child's `## Blocked by` links into the task graph; the **frontier** is the set of unblocked children. Read every body's full text and **all comments** — comments are part of the spec. Explicit children from the maintainer override discovery. Cap the batch at 3–5 tickets.

## Prior-work check

During resolution, for each child ticket:

- Search open PRs whose bodies reference it: `gh pr list --search "Closes #<N>" --state open`.
- Search branches matching `build/*<N>` or `afk/<N>`: `git branch -a --list "*<N>*"`.

Each finding gets a disposition at the batch gate:

- **satisfied** — the work already landed or is covered elsewhere; drop the ticket from the batch. A `## Blocked by` reference to a *closed* issue counts as satisfied without a search.
- **adopt** — the existing branch becomes that ticket's dispatch base; in batch mode it becomes the batch base. An adopted branch folds into this run's PR topology: the existing draft PR is superseded or absorbed, never left as a separate stacked PR.
- **rebuild** — ignore the prior work; normal dispatch from the batch base.

## Dispatch mechanics

- The dispatch contract is profile + filled task template → structured status JSON. Templates: `IMPLEMENTER_TASK.md` (implementers), `DESIGNER_TASK.md` (designer). Fill every `{{slot}}`.
- Profiles: `build-implementer` (`swe-2-high`, rounds 1–3), `build-implementer-max` (`swe-2-max`, rounds 4–5), `build-designer` (`swe-2-high`, read-only), `review-spec` (`swe-2-max`).
- Subagents get five tools — `read`, `edit`, `exec`, `grep`, `find_file_by_name` — and `edit` cannot create files. New files go through `exec` heredoc or `git apply`; `IMPLEMENTER_TASK.md` `## Tool reality` carries this for the implementer. `skill` and `ask_user_question` are unreachable inside a subagent — every context pointer is a file to read, and a question is a `BLOCKED`.
- Foreground is the default. A backgrounded subagent auto-denies ungranted tools: pre-approve `exec` in-session before fanning out, and cap background implementers at 2.
- `resume` a subagent (`resume` param with its agent id) for fix-ladder rounds 1–3 and for merge-conflict rebases — it always runs foreground, so previously denied grants can be approved inline.
- The implementer's status JSON routes the loop: `COMPLETE`/`COMPLETE_WITH_CONCERNS` → merge; `BLOCKED` → record `blocked_kind` (`context` | `capability` | `size` | `ambiguity`), mark its dependents blocked-by-predecessor (`context`), continue with unblocked tickets. Persist the implementer's full report under `reports/` and reference it by path.

## Run state

`.scratch/build-afk/<run>/` — `<run>` is the batch slug (e.g. `spec-208-213-214`):

- `STATUS.json` — machine-readable state: batch branch, `PRE_BUILD_SHA`, per-ticket `{status, branch, commit, fix_round, blocked_kind}`.
- `PROGRESS.md` — the ledger. First line names the spec. `Task <N>: complete` per merged ticket. A trailing `Task <N>: fix round <R>` line means resume mid-ladder at round `R+1`.
- `seams/<ticket>.md` — designer output, persisted verbatim.
- `reports/` — implementer reports, raw reviewer output (`<N>-review-<round>.md`), adjudication rulings, `run-retro.md`.

After compaction or a session break, trust the ledger and `git log` over session memory. On resume, refetch every issue's body and comments and flag any that changed mid-run — spec drift is surfaced to the maintainer, never silently built on. Reports stay on disk referenced by path; load them only to compose the PR body.

## Per-ticket review

After a ticket merges into the batch branch:

1. Dispatch `review-spec` with `REVIEW_BASE` = the batch SHA before the merge, `SPEC` = the ticket body plus comments, and `IMPLEMENTER CLAIMS` = the implementer's status JSON and concerns. Persist its raw output to `reports/<N>-review-<round>.md`.
2. Fix ladder, at most five rounds: rounds 1–3 `resume` the implementer with the findings; rounds 4–5 dispatch a fresh `build-implementer-max`. Each round re-reviews the new diff against the same base.
3. Adjudicate every open finding against `docs/agents/coding-standards.md`'s own tests. YAGNI is the load-bearing test: a finding whose benefit only materializes once the code shows a real need is non-load-bearing by definition. Contested or non-load-bearing findings park with a written ruling in `reports/` citing the standard. Load-bearing findings get the smallest change that unblocks dependents.
4. Minor findings never enter the ladder; park them for the final `/review`.
5. Stop for the maintainer only when every path forward is a guess.

## Run retro

After the final review, dispatch a read-only subagent (`build-designer` or `subagent_explore`) over the run directory — `STATUS.json`, `PROGRESS.md`, `seams/`, `reports/` — to write `reports/run-retro.md`: severity-ordered improvement candidates in the spirit of `.agents/skills/retro/SKILL.md`'s categories, plus the run-specific signals:

- `blocked_kind` clusters — under-specified tickets feed back to `/to-tickets`.
- Per-ticket fix-round counts — repeated round 4–5 escalations evidence the tier floor is wrong.
- Parked rulings — reviewer noise or gaps in `coding-standards.md`.
- Seam-gate escalation rate, permission denials, merge conflicts.

Surface the top candidates in the run summary next to the PR link. The deeper session-level pass stays a user-invoked `/retro`.

## Example

### Live acceptance run — spec #208

`/build-afk spec #208` with #213→#214 remaining:

- Resolution finds the #213→#214 chain; the prior-work check finds PRs #216/#218 → mark #211/#212 **satisfied**, **adopt** the #218 tip as the batch base.
- One batch gate, one design pass, one seam gate, serial implementers, one draft PR — while the maintainer watches.
