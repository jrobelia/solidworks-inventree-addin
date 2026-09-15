---
name: build-afk
description: "Autonomous build of reviewed, test-passing draft PRs from a parent spec (batched children), an explicit issue list (queued per-ticket PRs), or a label/milestone query (dependency-layered waves): consolidated seam gate, serial worktree implementers, five-round fix ladder. Invoke with /build-afk."
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
- Dispatch is serial and foreground by default. Once session tool grants exist it may fan out to at most 2 background implementers — a backgrounded subagent auto-denies every tool it is not pre-granted, so pre-approve `exec` first. Interrupting the session parks subagents rather than killing them; they resume on the next message.
- The batch cap is 3–5 tickets, inherited from `/build-hitl`. Spec and list intake larger than that asks the maintainer to split; query intake partitions into waves instead — the cap is a wave size, not a run limit.

## Loop

Do not move to the next step until the **Done when** criterion for the current step is met.

1. **Resolve the batch and hold the batch gate.** Resolve the intake shape to its finite set and merge topology per `REFERENCE.md` `## Inputs and merge topology`; read every ticket — body and all comments per `docs/agents/issue-tracker.md` `## Comments are part of the spec`. Compute the frontier. Run the prior-work check per `REFERENCE.md` `## Prior-work check`. Present the resolved set, its topology, and each ticket's prior-work disposition (satisfied / adopt / rebuild) to the maintainer once.
   **Done when:** the task graph, the frontier, the topology, and every prior-work disposition are resolved, and the maintainer has confirmed the batch.

2. **Set up the run.** Verify `git status --short` is clean — stop for the maintainer to commit or stash otherwise. Capture `PARENT_BRANCH` and `PRE_BUILD_SHA`. Create the batch branch per `docs/agents/pr-conventions.md` `## Branch names` (or check out the adopted base). Initialise `.scratch/build-afk/<run>/` per `REFERENCE.md` `## Run state`.
   **Done when:** the batch branch is checked out on a clean tree and the run directory exists with `STATUS.json` and `PROGRESS.md` initialised. Queue mode skips the batch branch — each ticket gets `build/issue-<N>` from `PARENT_BRANCH`.

3. **Design pass and seam gate.** Dispatch one `build-designer` per ticket (at most 2 concurrent) with `DESIGNER_TASK.md` filled. Persist each returned declaration verbatim to `seams/<ticket>.md`. Routine seams auto-approve. Collect every `architectural` proposal — new top-level module, changed consumed interface, ADR contradiction, ambiguous deletion test, or two equal candidates — into one consolidated seam gate for the maintainer: the last planned interruption before implementation. Apply `docs/agents/coding-standards.md` `## Module Design` when classifying; `/codebase-design` is reachable here in root.
   **Done when:** every ticket has a persisted `seams/<ticket>.md`, and the maintainer has ruled on every architectural proposal in a single gate.

4. **Implement → merge → review, one ticket at a time.** Pick the riskiest unblocked ticket first (architectural seam, integration point, unknowns), ties by issue order. For each ticket, per `REFERENCE.md` `## Dispatch mechanics` and `## Per-ticket review`:
   - Cut a worktree under `.worktrees/` on the ticket's run-plan branch: `afk/<ticket>` cut from batch HEAD (or the adopted base) in a batch, so dependents see their blockers' merged code; `build/issue-<N>` in a queue, cut from `PARENT_BRANCH` — or from a blocking sibling's `build/issue-<M>` when a `## Blocked by` edge exists.
   - Fill `IMPLEMENTER_TASK.md` and dispatch `build-implementer` in the foreground.
   - `COMPLETE` / `COMPLETE_WITH_CONCERNS` → merge `afk/<ticket>` into the batch branch. Merges happen in ticket order; under background fan-out a later finisher still waits for its predecessors. Conflicts resume the implementer in the foreground to rebase. Then run `dotnet test` on the batch branch — cross-ticket regressions and uncommitted drift surface here, not on the runner; a red run routes into the fix ladder rather than proceeding to the next ticket.
   - Per-ticket review: `review-spec` on the merged diff with `IMPLEMENTER CLAIMS`, then the five-round fix ladder — rounds 1–3 resume the implementer, rounds 4–5 dispatch a fresh `build-implementer-max`. Adjudicate each open finding against `docs/agents/coding-standards.md`'s own tests; park contested or non-load-bearing findings with a written ruling in `reports/`; minor findings never enter the ladder and park for the final review.
   - `BLOCKED` → record the `blocked_kind`; mark its dependents blocked-by-predecessor (`context`); continue with unblocked tickets.
   - Queue tickets skip the merge and collapse both reviews into one `AXES=both` `/review` on their own diff — the queue branch rules are under `## Queue and wave branches` below.
   - After the first merge, open the draft PR per `docs/agents/pr-conventions.md`; push each subsequent merge to it for visibility.
   **Done when:** every ticket is merged, blocked, or parked — each with its review, fix rounds, and rulings persisted — and `dotnet test` is green on the batch branch.

5. **Final review.** Run `/review` with `REVIEW_BASE` = `PRE_BUILD_SHA`, `SPEC_SOURCE` = the parent spec (body and comments), `AXES=both`. Fixes dispatch back through an implementer. Act on `REVIEW_STATUS` per `/review`'s output contract.
   **Done when:** `REVIEW_STATUS` is `clean`, `resolved`, or `deferred`, or the maintainer has been consulted on `escalated`/`capped`.

6. **Close out.** Confirm the pushed branch's checks are green on the self-hosted runner — Release-config tests, installer package, clean checkout; CI is the end-of-run gate, not a per-merge loop. Finalize the draft PR body per `docs/agents/pr-conventions.md` `## PR body` with the full `REVIEW_NOTES` under `### Review notes`. Dispatch the run retro per `REFERENCE.md` `## Run retro`. Deliver the run summary with the PR link and the top retro candidates, then hand off to `/qa`.
   **Done when:** checks are green, the draft PR body is complete, `reports/run-retro.md` is persisted, and the summary is delivered.

## Queue and wave branches

In **queue** mode there is no batch branch. Each ticket traverses step 4's dispatch on its own `build/issue-<N>`: the per-ticket and final reviews collapse into one `AXES=both` `/review` on `PARENT...build/issue-<N>` (the fix ladder and adjudication apply unchanged), `dotnet test` runs on the ticket branch, and the ticket's own draft PR is its closeout — step 5's batch review and step 6's single-PR finalization do not exist for a queue ticket.

In **wave** mode each wave traverses steps 2–6 as its own unit: the wave's tickets stand in for the batch, the final review's `SPEC_SOURCE` is the wave's ticket bodies and comments, and coherence inside the wave picks batch or queue topology per `REFERENCE.md` `## Inputs and merge topology`.

See [`REFERENCE.md`](REFERENCE.md) for intake resolution, the prior-work check, dispatch mechanics, run state, the per-ticket review and fix ladder, the run retro, and an example.
