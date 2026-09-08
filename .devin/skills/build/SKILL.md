---
name: build
description: "Build a reviewed, test-passing draft PR from a spec or tickets using /tdd and /review."
disable-model-invocation: true
triggers: ["user"]
---

# Build

`grill-with-docs → to-spec → to-tickets → build → qa`

## Inputs

`/build` can be invoked with a single ticket, a parent spec alone, or a parent spec with explicit child tickets. See `REFERENCE.md` `## Inputs and issue hierarchy` for how to resolve each case.

## Guardrails

- The current branch must be a feature branch; if it is `main`/`master`, stop and ask the user to check out a feature branch. The PR base is the current branch; warn the user and confirm the target, especially for chained PRs.
- Limit a batch to 3-5 tickets; if more are found, ask the user to split the work.
- Open a draft PR and hand off to `/qa`.

## Loop

Do not move to the next step until the **Done when** criterion for the current step is met.

1. Identify the parent spec and resolve the child-ticket **task graph** and **frontier** per `REFERENCE.md` `## Inputs and issue hierarchy`. Confirm the batch with the user.
   **Done when:** the parent spec, the task graph, and the frontier of unblocked child tickets are identified and the user has confirmed the batch.
2. Load the remaining **context pointers** in `REFERENCE.md` (`## Context pointers`) only when their branches fire.
   **Done when:** you can name which context pointers fired for this run and the design vocabulary has been consulted.
3. Verify the working tree is clean. If `git status --short` is non-empty, stop and ask the user to commit or stash their changes before `/build` starts.
   **Done when:** `git status --short` returns no output.
4. Capture the current branch as `PARENT_BRANCH` and the current commit as `PRE_BUILD_SHA`.
   **Done when:** both values are stored and visible.
5. Create the build branch from `PARENT_BRANCH` per `docs/agents/pr-conventions.md` `## Branch names`.
   **Done when:** the new branch exists, is checked out, and is based on `PARENT_BRANCH`.
6. **Run the `/tdd` red-green loop for each ticket** in frontier order (unblocked tickets first):
   - **Capture `PRE_TICKET_SHA`.** Before any code changes for this ticket, run `git rev-parse HEAD` and store it as `PRE_TICKET_SHA` for this ticket's per-ticket review.
   - **Propose the public seam and justify its depth.** Before proposing, read `docs/agents/coding-standards.md` `## Module Design` and consult the `/codebase-design` skill it points to. State the seam declaration required by `## Module Design` (public interface, production and test adapters, deletion-test result). If the interface is nearly as complex as the implementation, the seam is shallow — go back and find a deeper cut. If two or more seams are equally good, present the candidates with the same seam declaration and ask which to use; otherwise pause and ask the user to confirm the recommended seam before proceeding.
   - **Run `/tdd` — red first, then green.** Invoke the `/tdd` skill and run the full red → green loop. If the ticket is build-system, CI, or documentation-only and the spec explicitly states no new unit tests, use the build and test commands from `docs/agents/coding-standards.md` `## Build & Test Commands` in place of the red → green loop and state why in the response. Either way, if the commands fail or `/tdd` exits still red, fix and re-run until green. If you cannot make it green, stop and ask.
   - Commit with a message that references the ticket. Default to one logical commit per ticket; use multiple commits only if the ticket has clearly separate logical steps and the user agrees. Include the parent spec reference in the first commit so the work traces back to the parent spec in the issue tracker.
   - For batches of more than one ticket, call `/review` on the ticket's own diff before starting the next ticket — `REVIEW_BASE` = `PRE_TICKET_SHA`, `SPEC_SOURCE` = the ticket body, `AXES` = `spec` — and resolve any spec gaps it returns. Skip it for a single-ticket build — the step-8 review covers the same diff.
   **Done when:** every ticket has a user-confirmed seam declaration, a green `/tdd` red-green loop (or documented build-only equivalent), passing build/test, a reference commit on the build branch, and — for multi-ticket batches — a per-ticket `/review` call whose `REVIEW_STATUS` permits proceeding.
7. Run the build and test commands once more. If either fails, fix before proceeding.
   **Done when:** both commands exit successfully on the full branch.
8. Call `/review` with `REVIEW_BASE` = `PRE_BUILD_SHA`, `SPEC_SOURCE` = the parent spec body, `AXES` = `both`. `/review` returns `REVIEW_STATUS` and `REVIEW_NOTES`.
   **Done when:** `/review` has returned `REVIEW_STATUS` and `REVIEW_NOTES`.
9. Act on `REVIEW_STATUS` per `/review`'s output contract.
   **Done when:** `REVIEW_STATUS` permits proceeding or the user has been consulted.
10. Push and open a draft PR to `PARENT_BRANCH` per `docs/agents/pr-conventions.md` `## PR body`.
    **Done when:** the branch is pushed and a draft PR is open.

See [`REFERENCE.md`](REFERENCE.md) for examples and `docs/agents/pr-conventions.md` for branch naming and PR body.
