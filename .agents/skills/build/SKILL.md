---
name: build
description: "Build a reviewed, test-passing draft PR from a spec or tickets using /tdd and /code-review."
disable-model-invocation: true
---

# Build

`/build` turns a spec and its child tickets into a reviewed, test-passing draft PR. It sits between `/to-tickets` and `/qa` and replaces `/implement`.

`grill-with-docs → to-spec → to-tickets → build → qa → triage`

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
- The PR base is the branch `/build` was invoked from.
- Open a draft PR and leave it for review.

## Loop

1. Identify the parent spec and child tickets from the user's input (see `REFERENCE.md` for the input cases and `docs/agents/issue-tracker.md` for the shared conventions). If only a parent spec is given, find its child issues and confirm the batch. If inputs are missing, ask.
2. Load the context files listed in `REFERENCE.md`.
3. Capture the current branch as `PARENT_BRANCH` and the current commit as `PRE_BUILD_SHA`.
4. Create the build branch from `PARENT_BRANCH` using the naming rules in `REFERENCE.md`.
5. For each ticket in dependency order:
   - Propose the public seam for this ticket in domain language. If there is a real trade-off, present the options; otherwise proceed with the recommended seam after confirming with the user.
   - Run `/tdd`.
   - Run the build and test commands from `REFERENCE.md`. If either fails, fix before proceeding.
   - Commit with a message that references the ticket. One commit per ticket. Include the parent spec reference in the first commit so `/code-review` can locate it.
6. Run the build and test commands once more. If either fails, fix before proceeding.
7. Run `/code-review` from `PRE_BUILD_SHA`, pre-computing the diff and source material per `REFERENCE.md`.
8. Verify each `/code-review` finding against the code and the spec, then classify and act on it following the review guide in `REFERENCE.md`. Continue until every finding is resolved, deferred, or escalated to the user.
9. Push and open a draft PR to `PARENT_BRANCH`.

See [`REFERENCE.md`](REFERENCE.md) for branch naming, code review invocation, review classification, PR body, diff-size guard, and examples.
