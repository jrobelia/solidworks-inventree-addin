---
name: build
description: "Implement a spec or tickets with /tdd and /code-review, then open a draft PR for /qa."
disable-model-invocation: true
---

# Build

`/build` turns a spec and its child tickets into a reviewed, test-passing draft PR. It sits between `/to-tickets` and `/qa` and replaces `/implement`.

`grill-with-docs → to-spec → to-tickets → build → qa → triage`

## When to run

Run when the user says `/build` or passes a parent spec and child tickets.

## Guardrails

- Run only from a feature branch; refuse `main`/`master`.
- Cap a batch at 3-5 tickets; ask the user to split larger sets.
- Do not overwrite an existing branch.
- The PR base is the branch `/build` was invoked from.

## Loop

1. Read the parent spec and child tickets.
2. Read `CONTEXT.md`, `docs/agents/domain.md`, `docs/agents/coding-standards.md`, and `docs/agents/issue-tracker.md`.
3. Create the branch: `build/issue-<N>` or `build/spec-<parent>-<count>` from `PARENT_BRANCH`.
4. For each ticket in dependency order:
   - Agree the public seams in domain language (use an explore subagent if unclear).
   - Run `/tdd`.
   - Run the repo's build/test commands.
   - Commit.
5. Run the build/test commands once more.
6. Run `/code-review` over the full diff since the commit before the branch.
7. Classify each finding as **RED / YELLOW / GREEN** and act on it.
8. Push and open a draft PR to `PARENT_BRANCH`.

See [`REFERENCE.md`](REFERENCE.md) for branch naming, build/test commands, review classification, PR body, and diff-size guard.
