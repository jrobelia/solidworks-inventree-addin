---
name: build
description: "Build a reviewed, test-passing draft PR from a spec or tickets using /tdd and /code-review."
disable-model-invocation: true
triggers: ["user"]
---

# Build

`/build` turns a spec and its child tickets into a reviewed, test-passing draft PR. It sits between `/to-tickets` and `/qa` and replaces `/implement`.

`grill-with-docs → to-spec → to-tickets → build → qa`

## Inputs

Run when the user says `/build` or passes a parent spec and child tickets.

`/build` can be invoked with:

1. A single ticket: `/build #N`.
2. A parent spec and explicit child tickets: `/build spec #44 with #45 #46` or any natural phrasing.
3. A parent spec alone: `/build spec #44` — the agent finds the linked child issues.

If the parent spec or child tickets are missing or ambiguous, ask the user.

## Guardrails

- The current branch must be a feature branch; if it is `main`/`master`, stop and ask the user to check out a feature branch.
- Limit a batch to 3-5 tickets; if more are found, ask the user to split the work.
- The PR base is the branch `/build` was invoked from. If `PARENT_BRANCH` is not `main`/`master`, warn the user and confirm they want a chained PR.
- Open a draft PR and hand off to `/qa`.

## Loop

Do not move to the next step until the **Done when** criterion for the current step is met.

1. Identify the parent spec and child tickets from the user's input (see `REFERENCE.md` for the input cases and `docs/agents/issue-tracker.md` for the shared conventions). If only a parent spec is given, find its child issues and confirm the batch. If inputs are missing, ask.
   **Done when:** the parent spec and all child tickets are identified and the user has confirmed the batch.
2. Load the context files listed in `REFERENCE.md`.
   **Done when:** every file in `REFERENCE.md` `## Context files` has been loaded.
3. Verify the working tree is clean. If `git status --short` is non-empty, stop and ask the user to commit or stash their changes before `/build` starts.
   **Done when:** `git status --short` returns no output.
4. Capture the current branch as `PARENT_BRANCH` and the current commit as `PRE_BUILD_SHA`.
   **Done when:** both values are stored and visible.
5. Create the build branch from `PARENT_BRANCH` using the naming rules in `REFERENCE.md`.
   **Done when:** the new branch exists, is checked out, and is based on `PARENT_BRANCH`.
6. **Run the `/tdd` red-green loop for each ticket** in dependency order:
   - **Propose the public seam.** State the recommended seam in domain language and give a one-sentence rationale. If two or more seams are equally good, present the candidates and ask which to use; otherwise pause and ask the user to confirm the recommended seam before proceeding.
   - **Run `/tdd` — red first, then green.** Invoke the `/tdd` skill and do not skip the red → green loop. If the ticket is build-system, CI, or documentation-only and the spec explicitly states no new unit tests, run the build and test commands from `REFERENCE.md` in place of the `/tdd` red-green loop and state why in the response. If `/tdd` exits with failing tests, fix the failures and re-run it before proceeding. If you cannot make it green, stop and ask.
   - Run the build and test commands from `REFERENCE.md`. If either fails, fix before proceeding.
   - Commit with a message that references the ticket. Default to one logical commit per ticket; use multiple commits only if the ticket has clearly separate logical steps and the user agrees. Include the parent spec reference in the first commit so `/code-review` can locate it.
   **Done when:** every ticket has a user-confirmed seam, a green `/tdd` red-green loop (or documented build-only equivalent), passing build/test, and a reference commit on the build branch.
7. Run the build and test commands once more. If either fails, fix before proceeding.
   **Done when:** both commands exit successfully on the full branch.
8. Run the two-axis review from `PRE_BUILD_SHA` per `REFERENCE.md`. Pre-compute the diff, commit list, standards context, and originating spec context. Use the `run_subagent` path when it is available; otherwise use the Devin cloud child-session fallback in `REFERENCE.md`. Aggregate the `## Standards` and `## Spec` findings.
   **Done when:** the Standards and Spec findings have been returned.
9. Verify each Standards and Spec finding against the code and the spec, then classify and act on it following the review guide in `REFERENCE.md`. Continue until every finding is resolved, deferred, or escalated to the user.
   **Done when:** every finding is resolved, deferred, or escalated, or the two-pass cap in `REFERENCE.md` has been reached.
10. Push and open a draft PR to `PARENT_BRANCH`.
    **Done when:** the branch is pushed and a draft PR is open.

See [`REFERENCE.md`](REFERENCE.md) for branch naming, code review invocation, review classification, PR body, diff-size guard, and examples.
