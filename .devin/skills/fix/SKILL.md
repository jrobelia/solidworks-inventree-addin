---
name: fix
description: "Fix a bug ticket and get it reviewed — commits in-place on the current branch when it already has an open PR, otherwise opens its own draft PR. Invoke with /fix #N."
disable-model-invocation: true
triggers: ["user"]
---

# /fix

## Inputs

`/fix #N` — a GitHub issue describing a bug. Fetch the full body and labels per `docs/agents/issue-tracker.md`. If the issue is missing or isn't a bug report, ask the user.

## Guardrails

- Never commit directly to `main`/`master`. If the current branch is `main`/`master`, branch off to `fix/issue-<N>` in step 4.
- The handoff is to `/qa`; `/fix` does not merge.

## Process

Do not move to the next step until the **Done when** criterion for the current step is met.

1. **Fetch the issue.** Read the title, body, and labels. Store them as the fix's spec.
   **Done when:** the issue is confirmed to be a bug report and its body is stored as the spec.
2. **Load the context pointers.** Read `docs/agents/coding-standards.md` (`## Module Design` and `## Build & Test Commands`) and `CONTEXT.md`/`docs/agents/domain.md` so the fix uses the repo's design vocabulary and domain language. If the fix will create, change, or remove a public seam, consult `/codebase-design` and state the seam declaration required by `## Module Design` before writing code.
   **Done when:** you can name which pointers fired and, if a seam is touched, the seam declaration is stated.
3. **Check for hard-bug signals.** If the title, body, or labels contain signals like `intermittent`, `flaky`, `race`, `no deterministic repro`, `root cause unknown`, or `performance regression`, invoke `/diagnosing-bugs` first. It is a full diagnose-and-fix loop. It can return three outcomes:
   - **Fix + regression test produced** — the bug has a correct seam. Use the fix and test from `/diagnosing-bugs` and proceed to branch selection.
   - **Missing or shallow seam** — pause and hand off to the user. `/improve-codebase-architecture` is a user-invoked skill that produces an HTML report of deepening opportunities; the human starts it. The bug PR waits for the architecture work rather than patching around it.
   - **Cannot build a tight red-capable loop** — stop and ask the user for the repro environment, redacted artifacts, or permission to instrument.
   **Done when:** the bug is routed to the correct path (normal, hard-bug fix, architecture handoff, or stop-and-ask).
4. **Pick the branch and capture `PRE_FIX_SHA`.** Run `git rev-parse HEAD` and store the result as `PRE_FIX_SHA` before any fix work lands. Then check whether the current branch already has an open PR:
   ```powershell
   gh pr list --state open --head <current-branch> --json number,url,body
   ```
   - **Open PR found** — the bug may block that PR. Check whether the bug relates to the PR's work: does the PR's body, spec, or changed files overlap with the bug? If yes, treat it as PR-blocking and work on the current branch, verify `git status --short` is clean or commit only the files the fix touches, and update the PR body with `Closes #N` at ship time. If the bug is unrelated or the overlap is ambiguous, stop and ask the user whether to branch off to `fix/issue-<N>` or still commit in-place.
   - **No open PR** — verify `git status --short` is clean (stop and ask otherwise), then create `fix/issue-<N>` from the current branch. `PRE_FIX_SHA` was already captured above. A draft PR opens at step 8.
   **Done when:** the branch is chosen, `PRE_FIX_SHA` is captured, and the working tree is ready for the fix commit.
5. **Fix.** The path depends on how you got here:
   - **Hard bug** — apply the fix and regression test produced by `/diagnosing-bugs`.
   - **Normal bug** — run `/tdd`: red first (a failing regression test that reproduces the bug), then green. If the bug can't be reproduced as a failing test, state why — an unreproducible red is a hard-bug signal. If `/diagnosing-bugs` has already run for this issue, stop and ask the user; otherwise go back to step 3 and invoke `/diagnosing-bugs`.
   **Done when:** the failing regression test passes and build/test are green.
6. **Commit and verify.** Run build and test per `docs/agents/coding-standards.md` `## Build & Test Commands`, fix failures, then commit referencing `#N`. `/review` measures a committed diff, so the commit must land before the review call.
   **Done when:** the fix is committed and build/test pass on the commit.
7. **Review.** Call `/review` with `REVIEW_BASE` = `PRE_FIX_SHA`, `SPEC_SOURCE` = the bug issue body, `AXES` = `both`. Act on `REVIEW_STATUS` per `/review`'s output contract; review fixes land as follow-up commits.
   **Done when:** `/review` has returned a `REVIEW_STATUS` that permits proceeding, or the user has been consulted on `escalated`/`capped`.
8. **Ship.** Push the branch. On an existing PR, append `Closes #N` and the `REVIEW_NOTES` to its body with `gh pr edit`. Otherwise open a draft PR following `build/REFERENCE.md` `## PR body`.
   **Done when:** the branch is pushed and the PR body is updated or the draft PR is open.

## Skills invoked

- `/diagnosing-bugs` — hard-bug diagnosis and fix loop.
- `/improve-codebase-architecture` — user-invoked; hand the seam finding to the user when diagnosis points there.
- `/codebase-design` — when the fix touches a public seam.
- `/tdd` — the red-green fix loop for normal bugs.
- `/review` — the shared two-axis review-and-fix loop.
