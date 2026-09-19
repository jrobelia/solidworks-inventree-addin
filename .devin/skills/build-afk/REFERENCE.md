# `/build-afk` reference

## Inputs and merge topology

Every intake shape resolves to a confirmed finite set plus a merge topology, presented together at the batch gate. An open-ended pull is rejected — the maintainer confirms a finite list, never a query's future results. For every ticket read the full body and **all comments** — comments are part of the spec — and resolve `## Blocked by` links into the task graph; the **frontier** is the unblocked set.

- **Parent spec** (`/build-afk spec #N`) — children via `## Parent` bodies, per `docs/agents/issue-tracker.md` `## Parent and child issues`. Spec children share one batch: `build/spec-<parent>-<children>` per `docs/agents/pr-conventions.md` `## Branch names`, one draft PR. Batch cap is 3–5 tickets.
- **Explicit issue list** (`/build-afk #45 #51 #63`) — fetch each ticket the same way. A list that shares a `## Parent` batches like a spec intake. With no shared `## Parent` the set runs as a **queue**: each ticket works on `build/issue-<N>` branched from `PARENT_BRANCH` and gets its own draft PR — unrelated changes never share a PR, and a stalled ticket holds nothing up. Queue tickets with no `## Blocked by` edges never see each other's code; a ticket blocked by a sibling branches from that sibling's `build/issue-<M>` at dispatch.
- **Label/milestone query** (`/build-afk --label <x>` / `--milestone <y>`) — resolve the full set once via `gh issue list --label`/`--milestone`, then partition into **3–5-ticket waves** by dependency layer — the cap is a wave size, not a run limit. Waves run sequentially, each traversing the loop (SKILL.md steps 2–6) and producing its own PR(s). Within a wave, coherence picks the topology: shared `## Parent` → batch, otherwise queue.

While resolving, cross-check each child's acceptance criteria against the parent's Implementation Decisions and user stories — the orchestrator is the only stage that reads both, so a parent↔child contradiction or an unspecified precedence/interaction rule can only be caught here. List each at the batch gate with a proposed ruling; the default ruling is that the child's finer-grained AC governs the parent's generality. A confirmed ruling propagates to the designer and implementer through `{{blocker_context}}` / `{{extra_context}}` or the seam note, and is appended to the `SPEC:` / `SPEC_SOURCE` handed to reviewers — downstream agents apply it as settled spec, never see the raw contradiction, and never flag ruling-compliant behaviour against the un-amended parent text.

The prior-work check below runs for every intake shape, not just spec intake.

Two intake shapes are out of scope and route to `/build-hitl` instead: tickets tracked as local files (`.scratch/<feature>/issues/` — the resolver assumes GitHub issue numbers, so a spec whose children aren't GitHub issues stops at resolution rather than failing mid-run) and expand–contract sequences whose intermediate tickets are intentionally not green alone — the per-ticket green gate can't defer greenness to an integrate ticket.

## Prior-work check

During resolution, for each child ticket:

- Search open PRs whose bodies reference it: `gh pr list --search "Closes #<N>" --state open --json number,title,url`. When a `Closes #N` line must come out of a PR body, project it — `gh pr view <N> --json body --jq '.body' | grep -i "closes #"` — rather than pulling full PR bodies into context.
- Search branches matching `build/*<N>` or `afk/<N>`: `git branch -a --list "build/*<N>*" --list "afk/<N>"`.
- When neither source finds anything, do not declare "no prior work" yet — closeout deletes `afk/<N>` branches, so an absent branch proves nothing. Fall back to merged ancestry — `git log <PARENT_BRANCH> --oneline --grep="#<N>"`, `git branch --contains <sha>`, or patch-id comparison against `PARENT_BRANCH` — and to prior run ledgers (`.scratch/build-afk/*/STATUS.json` commit fields).

Each finding gets a disposition at the batch gate — record the disposition and its source in `PROGRESS.md`:

- **satisfied** — the work already landed or is covered elsewhere; drop the ticket from the batch. Separately, a `## Blocked by` reference to a *closed* issue is a satisfied edge — the dependent is unblocked without a search — but the ticket itself still needs its own disposition; a closed blocker does not imply the dependent's work exists.
- **adopt** — the existing branch becomes that ticket's dispatch base; in batch mode it becomes the batch base. An adopted branch folds into this run's PR topology: the existing draft PR is superseded or absorbed, never left as a separate stacked PR. When the adopted base's merge-base with `PARENT_BRANCH` is behind it, merge `PARENT_BRANCH` into the batch branch at setup so implementers read current docs and the closeout merge is clean by construction — never rebase: the adopted commits are referenced by the folded PRs.
- **rebuild** — ignore the prior work; normal dispatch from the batch base.

## Dispatch mechanics

### Contract and tool reality

- The dispatch contract is profile + filled task template → structured status JSON. Templates: `IMPLEMENTER_TASK.md` (implementers), `DESIGNER_TASK.md` (designer). Fill every `{{slot}}`.
- Profiles: `build-implementer` (`swe-2-high`, rounds 1–3), `build-implementer-max` (`swe-2-max`, rounds 4–5), `build-designer` (`swe-2-high`, read-only), `review-spec` (`swe-2-max`).
- Subagents get five tools — `read`, `edit`, `exec`, `grep`, `glob` (exposed as `find_file_by_name`) — and `edit` cannot create files. New files go through `exec` heredoc or `git apply`; `IMPLEMENTER_TASK.md` `## Tool reality` carries this for the implementer. `skill` and `ask_user_question` are unreachable inside a subagent — every context pointer is a file to read, and a question is a `BLOCKED`.

### Mode and grants

- Background is the default: a unit dispatches `is_background: true` as soon as it's eligible, within the caps below. Foreground is the exception — `resume` dispatches (below), and a profile whose needed grants are missing, which either dispatches foreground once to surface the prompts or drops the run to declared serial when the maintainer won't grant.
- Grants are checked mechanically before a background dispatch, because a backgrounded subagent auto-denies ungranted tools: a `build-implementer` needs `exec` + `edit`, `review-spec`/`review-standards` need `exec` — confirmed against `.devin/config*.json` allow rules plus session grants. `build-designer` and the retro agent need nothing — read-only tools auto-approve in every mode, so they background unconditionally. When a denial stalls a background subagent, foreground it from the subagent panel (`f` on the running entry) or resume it.
- `resume` a subagent (`resume` param with its agent id) for fix-ladder rounds 1–3 and for merge-conflict rebases — it always runs foreground, so previously denied grants can be approved inline.

### Concurrency and the serial spine

- Concurrency caps: designers fan out to 5 — read-only, they cannot conflict. Background implementers cap at 3, raised to 5 across an *independent* set: no `## Blocked by` path between the tickets and, in batch mode, disjoint `seams/<ticket>.md` footprints — no shared top-level module or consumed interface. Queue tickets are structurally independent — separate branches and PRs — so a queue runs at the higher cap without the seam test. Dependents still wait for their blockers to merge, so the frontier bounds concurrency before the cap does.
- The serial spine: merges happen in ticket order — a later finisher waits for its predecessors — and each batch merge is followed by `dotnet test` on the batch branch. A red run holds further merges and fresh ticket dispatches while the fix ladder clears it; in-flight work continues, and a queue ticket's red holds only itself.
- A backgrounded reviewer never diffs a moving HEAD: pin the end of the range with `REVIEW_HEAD` — the merge SHA for a batch per-ticket review, the ticket tip for a queue review, the batch tip for the final review. Fix-ladder re-reviews re-pin to the new tip each round.

### Routing

- The implementer's status JSON routes the loop: `COMPLETE`/`COMPLETE_WITH_CONCERNS` → merge; `BLOCKED` → record `blocked_kind` (`context` | `capability` | `size` | `ambiguity`), mark its dependents blocked-by-predecessor (`context`), continue with unblocked tickets. Persist the implementer's full report under `reports/` and reference it by path.

## Run state

`.scratch/build-afk/<run>/` — `<run>` is the batch slug (e.g. `spec-208-213-214`):

- `STATUS.json` — machine-readable state: batch branch, `PRE_BUILD_SHA`, per-ticket `{phase, status, branch, worktree, commit, fix_round, blocked_kind}` plus `entered_at` — an ISO-8601 stamp recording when the current phase began; the full transition trail lives in `PROGRESS.md`'s timestamped entries, so dispatch-to-artifact gaps are queryable without git archaeology.
- `PROGRESS.md` — the ledger. First line names the spec. Every entry carries an `HH:MM` local (or ISO-8601) timestamp prefix — merges, review verdicts, fix rounds, gate decisions; a timestamp on write, no timing machinery — a real stamp, never a placeholder: the field exists to make dispatch-to-artifact gaps queryable. `Task <N>: complete` per merged ticket. A trailing `Task <N>: fix round <R>` line means resume mid-ladder at round `R+1`.
- `seams/<ticket>.md` — designer output, persisted verbatim.
- `reports/` — implementer reports, reviewer output (`<N>-review-<round>.md` per ticket, `<axis>-review-<pass>.md` for the final review), adjudication rulings, `run-retro.md`.

After compaction or a session break, trust the ledger and `git log` over session memory. On resume, refetch every issue's body and comments and flag any that changed mid-run — spec drift is surfaced to the maintainer, never silently built on. Reports stay on disk referenced by path — the status JSON is the routing signal: load `reports/<N>-implementer.md` only on `BLOCKED` or `COMPLETE_WITH_CONCERNS` (the detail lives in the report) and at closeout to compose the PR body; adjudicate reviews from the digest and open the persisted file only when the digest can't settle a ruling. Resuming a mid-ladder ticket needs the implementer's live agent handle, which may not survive a break — if it is unresolvable, dispatch a fresh `build-implementer` on the same worktree with the persisted findings; the round count still applies.

## Per-ticket review

Queue tickets never merge into a batch branch — for them this review and the final review collapse into one `AXES=both` `/review` on `PARENT...build/issue-<N>` with `REVIEW_HEAD` = the ticket tip (so the range doesn't depend on the orchestrator's checkout) and `REPORT_DIR` = the run's `reports/`; the fix ladder, adjudication, and rulings below apply unchanged.

After a ticket merges into the batch branch:

1. Dispatch `review-spec` in the background with `REVIEW_BASE` = the batch SHA before the merge, `REVIEW_HEAD` = the merge SHA, `SPEC` = the ticket body plus comments, `IMPLEMENTER CLAIMS` = the implementer's status JSON and concerns, `SUITE RESULT` = the post-merge `dotnet test` result line, and `REPORT_PATH` = `reports/<N>-review-<round>.md` — the reviewer writes its full findings there itself and returns an adjudication digest (severity + file:line + spec quote per finding), so review text never re-transits the orchestrator's context.
2. Fix ladder, at most five rounds: rounds 1–3 `resume` the implementer with the findings; rounds 4–5 dispatch a fresh `build-implementer-max`. Each round re-reviews the new diff against the same base, re-pinning `REVIEW_HEAD` to the new tip. Once `afk/<N>` has merged into the batch branch, fix rounds add new commits — never amend or rebase the merged tip — and `STATUS.json`'s `commit` records the batch-branch merge SHA, not the worktree tip.
3. Adjudicate every open finding against `docs/agents/coding-standards.md`'s own tests. YAGNI is the load-bearing test: a finding whose benefit only materializes once the code shows a real need is non-load-bearing by definition. Contested or non-load-bearing findings park with a written ruling in `reports/` citing the standard. Load-bearing findings get the smallest change that unblocks dependents.
4. Minor findings never enter the ladder; park them for the final `/review`.
5. Stop for the maintainer only when every path forward is a guess.

## Run retro

After the final review, dispatch a read-only subagent (`build-designer` or `subagent_explore`) in the background — it needs no grant and overlaps the closeout — over the run directory — `STATUS.json`, `PROGRESS.md`, `seams/`, `reports/` — to write `reports/run-retro.md`: severity-ordered improvement candidates in the spirit of `.agents/skills/retro/SKILL.md`'s categories, plus the run-specific signals:

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
