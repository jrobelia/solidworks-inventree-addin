---
name: build
description: "Build a reviewed, test-passing draft PR from a spec or tickets using /tdd and /code-review."
disable-model-invocation: true
---

# Build

`/build` turns a spec and its child tickets into a reviewed, test-passing draft PR. It sits between `/to-tickets` and `/qa` and replaces `/implement`.

`grill-with-docs → to-spec → to-tickets → build → qa → triage`

## When to run

Run when the user says `/build` or passes a parent spec and child tickets.

## Guardrails

- The current branch must be a feature branch; if it is `main`/`master`, stop and ask the user to check out a feature branch.
- Limit a batch to 3-5 tickets; if more are provided, ask the user to split the work.
- The PR base is the branch `/build` was invoked from.
- Open a draft PR; do not merge.

## Loop

1. Fetch the parent spec and child tickets into the session.
2. Load the context files listed in `REFERENCE.md`.
3. Capture the current branch as `PARENT_BRANCH` and the current commit as `PRE_BUILD_SHA`.
4. Create the build branch from `PARENT_BRANCH` using the naming rules in `REFERENCE.md`.
5. For each ticket in dependency order:
   - Propose the public seam for this ticket in domain language. If there is a real trade-off, present the options; otherwise proceed with the recommended seam after confirming with the user.
   - Run `/tdd`.
   - Run the build/test commands from `REFERENCE.md`. If they fail, fix before proceeding.
   - Commit with a message that references the ticket. Include the parent spec reference in the first commit so `/code-review` can locate it.
6. Run the build/test commands once more. If they fail, fix before proceeding.
7. Run `/code-review` over `git diff <PRE_BUILD_SHA>...HEAD`.
8. Verify each `/code-review` finding against the code and the spec, then classify and act on it following the review guide in `REFERENCE.md`. Continue until every finding is resolved, deferred, or escalated to the user.
9. Push and open a draft PR to `PARENT_BRANCH`.

See [`REFERENCE.md`](REFERENCE.md) for branch naming, build/test commands, review classification, PR body, and diff-size guard.
