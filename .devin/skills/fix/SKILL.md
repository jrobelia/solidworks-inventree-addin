---
name: fix
description: "Fix a bug ticket and get it reviewed — commits in-place on the current branch when it already has an open PR, otherwise opens its own draft PR. Invoke with /fix #N."
disable-model-invocation: true
triggers: ["user"]
---

# /fix

`/fix` is the bug-to-PR counterpart of `/build`: it takes a bug issue to a reviewed, test-passing state and lands it on whichever branch the fix belongs to.

## Inputs

`/fix #N` — a GitHub issue describing a bug. Fetch the full body and labels per `docs/agents/issue-tracker.md`. If the issue is missing or isn't a bug report, ask the user.

## Guardrails

- Never commit to `main`/`master`. If the current branch is `main`/`master`, it cannot have a PR-blocking bug target — branch off it in step 4.
- The handoff is to `/qa`. Do not merge.

## Process

1. **Fetch the issue.** Read the title, body, and labels. Store them as the fix's spec.
2. **Load the context pointers.** Read `docs/agents/coding-standards.md` (`## Module Design` and `## Build & Test Commands`) and `CONTEXT.md`/`docs/agents/domain.md` so the fix uses the repo's design vocabulary and domain language. If the fix will create, change, or remove a public seam, consult `/codebase-design` and state the public interface, the production and test adapters, and the deletion-test result before writing code.
3. **Check for hard-bug signals.** If the title, body, or labels contain signals like `intermittent`, `flaky`, `race`, `no deterministic repro`, `root cause unknown`, or `performance regression`, invoke `/diagnosing-bugs` first. It is a full diagnose-and-fix loop, not just a pre-step. It can return three outcomes:
   - **Fix + regression test produced** — the bug has a correct seam. Use the fix and test from `/diagnosing-bugs` and proceed to branch selection.
   - **Missing or shallow seam** — pause and hand off to the user. `/improve-codebase-architecture` is a user-invoked skill that produces an HTML report of deepening opportunities; the human starts it. The bug PR waits for the architecture work rather than patching around it.
   - **Cannot build a tight red-capable loop** — stop and ask the user for the repro environment, redacted artifacts, or permission to instrument.
4. **Pick the branch and capture `PRE_FIX_SHA`.** Run `git rev-parse HEAD` and store the result as `PRE_FIX_SHA` before any fix work lands. Then check whether the current branch already has an open PR:
   ```powershell
   gh pr list --state open --head <current-branch> --json number,url,body
   ```
   - **Open PR found** — the bug may block that PR. Check whether the bug relates to the PR's work: does the PR's body, spec, or changed files overlap with the bug? If yes, treat it as PR-blocking and work on the current branch, commit in-place, and update the PR body with `Closes #N` at ship time. If the bug is unrelated or the overlap is ambiguous, stop and ask the user whether to branch off to `fix/issue-<N>` or still commit in-place.
   - **No open PR** — verify `git status --short` is clean (stop and ask otherwise), then create `fix/issue-<N>` from the current branch. `PRE_FIX_SHA` was already captured above. A draft PR opens at step 7.
5. **Fix.** The path depends on how you got here:
   - **Hard bug** — apply the fix and regression test produced by `/diagnosing-bugs`.
   - **Normal bug** — run `/tdd`: red first (a failing regression test that reproduces the bug), then green. If the bug can't be reproduced as a failing test, state why — an unreproducible red is a hard-bug signal. If `/diagnosing-bugs` has already run for this issue, stop and ask the user; otherwise go back to step 3 and invoke `/diagnosing-bugs`.
6. **Commit and verify.** Run build and test per `docs/agents/coding-standards.md` `## Build & Test Commands`, fix failures, then commit referencing `#N`. `/review` measures a committed diff, so the commit must land before the review call.
7. **Review.** Call `/review` with `REVIEW_BASE` = `PRE_FIX_SHA`, `SPEC_SOURCE` = the bug issue body, `AXES` = `both`. Act on `REVIEW_STATUS` per `/review`'s output contract; review fixes land as follow-up commits.
8. **Ship.** Push the branch. On an existing PR, append `Closes #N` and the review notes to its body with `gh pr edit`. Otherwise open a draft PR: `Closes #N`, the root cause in one line, the regression test added, build/test commands run, and `REVIEW_NOTES` under `### Review notes` and `### Deferred and follow-up issues`. End the PR body with: `Run /qa on this branch. /qa will take the PR out of draft if QA passes and ask whether to merge.`

## Skills invoked

- `/diagnosing-bugs` — hard-bug diagnosis and fix loop.
- `/improve-codebase-architecture` — user-invoked; hand the seam finding to the user when diagnosis points there.
- `/codebase-design` — when the fix touches a public seam.
- `/tdd` — the red-green fix loop for normal bugs.
- `/review` — the shared two-axis review-and-fix loop.
