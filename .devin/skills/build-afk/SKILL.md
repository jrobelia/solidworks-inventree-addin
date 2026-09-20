---
name: build-afk
description: "Autonomous build of reviewed, test-passing draft PRs from a parent spec (batched children), an explicit issue list (queued per-ticket PRs), or a label/milestone query (dependency-layered waves): consolidated seam gate, worktree implementers with capped fan-out, five-round fix ladder. Invoke with /build-afk."
disable-model-invocation: true
triggers: ["user"]
---

# `/build-afk`

`grill-with-docs → to-spec → to-tickets → build-hitl | build-afk → qa`

## Inputs

`/build-afk` takes a parent spec, an explicit issue list, or a label/milestone query. Every shape resolves to a confirmed finite set plus a merge topology — batch, queue, or waves — before the batch gate; open-ended pulls are rejected. `REFERENCE.md` `## Inputs and merge topology` owns the resolution rules.

## Guardrails

- If `run_subagent`/`read_subagent` are unavailable (subagents disabled or admin-set to None), stop and route to `/build-hitl` — this skill is an orchestrator and cannot run without dispatch.
- Profiles and skills snapshot at session start. A session that authored or edited `.devin/agents/` profiles cannot reliably dispatch them — after installing or changing profiles, run `/build-afk` in a fresh session.
- Two planned interruptions only: the batch gate (step 1) and the seam gate (step 3). Beyond those, stop for the maintainer only on irreversible or destructive operations, security-sensitive actions, side effects outside the worktrees (the push, the PR), or a spec so broken every path forward is a guess. Everything else is ruled on and recorded.
- Dispatch defaults to background fan-out within the caps in `REFERENCE.md` `## Dispatch mechanics` — a unit goes `is_background: true` as soon as it's eligible. Foreground is the exception: resumes and conflict rebases always run foreground (a denied grant approves inline there), and a profile whose needed grants are missing — `exec` + `edit` for implementers, `exec` for reviewers — dispatches foreground once to acquire them, or the run declares serial when the maintainer won't grant. Interrupting the session parks subagents rather than killing them; they resume on the next message.
- The batch cap is 3–5 tickets, inherited from `/build-hitl`. Spec and list intake larger than that asks the maintainer to split; query intake partitions into waves instead — the cap is a wave size, not a run limit.

## Loop

Do not move to the next step until the **Done when** criterion for the current step is met.

1. **Resolve the batch and hold the batch gate.** Resolve the intake shape to its finite set and merge topology per `REFERENCE.md` `## Inputs and merge topology`; read every ticket — body and all comments per `docs/agents/issue-tracker.md` `## Comments are part of the spec`; cross-check child tickets against the parent for contradictions and unspecified precedence per the same section, each surfacing as a proposed ruling. Compute the frontier. Run the prior-work check per `REFERENCE.md` `## Prior-work check`. Present the resolved set, its topology, each ticket's prior-work disposition (satisfied / adopt / rebuild), every proposed ruling, and the run's dispatch mode to the maintainer once — the fan-out plan (which units overlap, at what cap) when the needed grants exist, `serial dispatch this run` stated plainly when they don't, rather than implying fan-out that can't happen.
   **Done when:** the task graph, the frontier, the topology, every prior-work disposition, and the proposed rulings are resolved, and the maintainer has confirmed the batch.

2. **Set up the run.** Verify `git status --short` is clean — stop for the maintainer to commit or stash otherwise. Capture `PARENT_BRANCH` and `PRE_BUILD_SHA`. Create the batch branch per `docs/agents/pr-conventions.md` `## Branch names` (or check out the adopted base — when it is behind `PARENT_BRANCH`, merge `PARENT_BRANCH` in now per `REFERENCE.md` `## Prior-work check`; never rebase). Confirm the tool grants each subagent profile needs before the design pass — `exec` + `edit` for implementers, `exec` for reviewers; `build-designer` is read-only and needs none — against `.devin/config*.json` allow rules and session grants, requesting them from the maintainer where coverage is missing; a grant confirmed here upgrades a serial declaration made at the gate, and without one the run stays serial. Initialise `.scratch/build-afk/<run>/` per `REFERENCE.md` `## Run state`.
   **Done when:** the batch branch is checked out on a clean tree, the grant outcome is recorded in the ledger, and the run directory exists with `STATUS.json` and `PROGRESS.md` initialised. Queue mode follows `## Queue and wave branches` — no batch branch exists.

3. **Design pass and seam gate.** Dispatch one `build-designer` per ticket — all in the background at once; the profile is read-only so nothing it calls can be denied, and cap 5 covers the batch — with `DESIGNER_TASK.md` filled. Persist each returned declaration verbatim to `seams/<ticket>.md`. Routine seams auto-approve — including a consumed-interface change whose shape the ticket or an ADR fixes verbatim, which carries an "interface change" annotation instead of escalating. Collect every `architectural` proposal — new top-level module, consumed-interface change with an open decision, ADR contradiction, ambiguous deletion test, or two equal candidates — into one consolidated seam gate for the maintainer: the last planned interruption before implementation. Annotated flags ride as a notice list — presented at the gate if one happens, otherwise recorded in the ledger and surfaced in the run summary; a run whose flags are all notices does not interrupt. A routine flag whose declaration changes a consumed interface joins the notice list even when the annotation is missing — don't let a dropped annotation silently drop the notice. Apply `docs/agents/coding-standards.md` `## Module Design` when classifying; `/codebase-design` is reachable here in root.
   **Done when:** every ticket has a persisted `seams/<ticket>.md`, and the maintainer has ruled on every architectural proposal in a single gate.

4. **Implement → merge → review.** Keep up to the cap of `build-implementer` dispatches in flight over the frontier — riskiest unblocked first (architectural seam, integration point, unknowns), ties by issue order — each per `REFERENCE.md` `## Dispatch mechanics` and `## Per-ticket review`:
   - Cut a worktree under `.worktrees/` on the ticket's run-plan branch: `afk/<ticket>` cut from batch HEAD (or the adopted base) in a batch, so dependents see their blockers' merged code; the queue's `build/issue-<N>` branches per `## Queue and wave branches`.
   - Fill `IMPLEMENTER_TASK.md` and dispatch `build-implementer` in the background.
   - `COMPLETE` / `COMPLETE_WITH_CONCERNS` → merge `afk/<ticket>` into the batch branch and run `dotnet test` on it — cross-ticket regressions and uncommitted drift surface here, not on the runner. Merge order, conflict rebases, and the red gate follow the serial spine in `## Dispatch mechanics`.
   - Per-ticket review, fix ladder, and adjudication per `## Per-ticket review`: dispatch `review-spec` in the background with `IMPLEMENTER CLAIMS` and `REVIEW_HEAD` pinned, so the review overlaps in-flight implementation.
   - `BLOCKED` → record the `blocked_kind`; mark its dependents blocked-by-predecessor (`context`); continue with unblocked tickets.
   - Queue tickets follow `## Queue and wave branches` — no merge, one collapsed `AXES=both` `/review` on their own diff.
   - After the first merge, open the draft PR per `docs/agents/pr-conventions.md`; push each subsequent merge to it for visibility.
   **Done when:** every ticket is merged, blocked, or parked — each with its review, fix rounds, and rulings persisted — and `dotnet test` is green on the batch branch.

5. **Final review.** Run `/review` with `REVIEW_BASE` = `PRE_BUILD_SHA`, `REVIEW_HEAD` = the batch tip, `SPEC_SOURCE` = the parent spec (body and comments), `AXES=both`, `REPORT_DIR` = the run's `reports/` so each axis self-persists and returns a digest, `SUITE_RESULT` = the latest batch-branch `dotnet test` result. Fixes dispatch back through an implementer; the ticket branch is already merged, so fixes land as new commits — never amend or rebase the merged tip. Act on `REVIEW_STATUS` per `/review`'s output contract.
   **Done when:** `REVIEW_STATUS` is `clean`, `resolved`, or `deferred`, or the maintainer has been consulted on `escalated`/`capped`.

6. **Close out.** Confirm the pushed branch's checks are green on the self-hosted runner — Release-config tests, installer package, clean checkout; CI is the end-of-run gate, not a per-merge loop. Finalize the draft PR body per `docs/agents/pr-conventions.md` `## PR body` with the full `REVIEW_NOTES` under `### Review notes`. Remove the `.worktrees/` worktrees and delete the `afk/<ticket>` branches once the PR is open — stale `afk/` branches false-positive the prior-work check on later runs. Write each ticket's terminal state to `STATUS.json` — `phase`, `status`, `commit`, `pr`, `fix_round`, `review_status`, `ci` — before dispatching the retro, which reads this file and must see settled state. Dispatch the run retro per `REFERENCE.md` `## Run retro`. Deliver the run summary with the PR link and the top retro candidates, then hand off to `/qa`.
   **Done when:** checks are green, the draft PR body is complete, terminal `STATUS.json` state written, worktrees removed and `afk/` branches deleted, `reports/run-retro.md` is persisted, and the summary is delivered.

## Queue and wave branches

In **queue** mode there is no batch branch and nothing merges — the deltas from the batch path:

- **Branch** — each ticket gets `build/issue-<N>` from `PARENT_BRANCH`, or from a blocking sibling's `build/issue-<M>` when a `## Blocked by` edge exists; step 2's batch-branch work is skipped.
- **Concurrency** — tickets are structurally independent (separate branches and PRs), so the higher implementer cap applies without the seam test.
- **Test** — `dotnet test` runs on the ticket branch; a red run holds that ticket alone, never the queue.
- **Review** — the per-ticket and final reviews collapse into one `AXES=both` `/review` on `PARENT...build/issue-<N>` per `REFERENCE.md` `## Per-ticket review` — the fix ladder and adjudication apply unchanged; step 5's batch review does not exist.
- **Closeout** — the ticket's own draft PR is its closeout per step 6: pushed-branch checks green (the end-of-run gate), the body finalized with `REVIEW_NOTES`, the `/qa` handoff per ticket. Diff the changed-file lists across the queue's branches: when two PRs touch the same files, record the expected merge conflict in the later PR's body or the run summary — "no merges" defers conflicts to the milestone merge rather than eliminating them.

In **wave** mode each wave traverses steps 2–6 as its own unit: the wave's tickets stand in for the batch, the final review's `SPEC_SOURCE` is the wave's ticket bodies and comments, and coherence inside the wave picks batch or queue topology per `REFERENCE.md` `## Inputs and merge topology`.

See [`REFERENCE.md`](REFERENCE.md) for intake resolution, the prior-work check, dispatch mechanics, run state, the per-ticket review and fix ladder, the run retro, and an example.
