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

- Never commit to `main`/`master`. If the current branch is `main`/`master`, it cannot have a PR-blocking bug target — branch off it in step 3.
- The handoff is to `/qa`. Do not merge.

## Process

1. **Fetch the issue.** Read the title, body, and labels. Store them as the fix's spec.
2. **Check for hard-bug signals.** If the title, body, or labels contain signals like `intermittent`, `flaky`, `race`, `no deterministic repro`, `root cause unknown`, or `performance regression`, invoke `/diagnosing-bugs` before touching code — the fix must target the diagnosed cause, not the symptom. If the diagnosis shows the root cause is a missing or shallow seam, pause here and hand off to the user: `/improve-codebase-architecture` is a user-invoked skill, so the human starts it. The bug PR waits for the architecture work rather than patching around it.
3. **Pick the branch.** Check whether the current branch already has an open PR:
   ```powershell
   gh pr list --state open --head <current-branch> --json number,url,body
   ```
   - **Open PR found** — the bug blocks that PR: work on the current branch and commit in-place; the `Closes #N` body update happens at ship time. Store the commit before your work as `PRE_FIX_SHA`.
   - **No open PR** — verify `git status --short` is clean (stop and ask otherwise), then create `fix/issue-<N>` from the current branch and capture `PRE_FIX_SHA`. A draft PR opens at step 7.
4. **Fix with `/tdd`.** Red first: a failing regression test that reproduces the bug, then green. If the bug can't be reproduced as a failing test, state why and fall back to the build/test commands from `docs/agents/coding-standards.md` — an unreproducible red is itself a hard-bug signal; go back to step 2 if you skipped it.
5. **Commit the fix.** Run build and test per `docs/agents/coding-standards.md`, fix failures, then commit referencing `#N`. `/review` measures a committed diff, so the commit must land before the review call.
6. **Review.** Call `/review` with `REVIEW_BASE` = `PRE_FIX_SHA`, `SPEC_SOURCE` = the bug issue body, `AXES` = `both`. Act on the returned `REVIEW_STATUS`: proceed on `clean`/`resolved`/`deferred`, keep the PR in draft and stop for the user on `escalated`, ask the user on `capped`. Review fixes land as follow-up commits.
7. **Ship.** Push the branch. On an existing PR, append `Closes #N` and the review notes to its body with `gh pr edit`. Otherwise open a draft PR: `Closes #N`, the root cause in one line, the regression test added, build/test commands run, and `REVIEW_NOTES` under `### Review notes` and `### Deferred and follow-up issues`. End the PR body with: `Run /qa on this branch. /qa will take the PR out of draft if QA passes and ask whether to merge.`

## Skills invoked

- `/diagnosing-bugs` — hard-bug diagnosis before fixing.
- `/improve-codebase-architecture` — user-invoked; hand the seam finding to the user when diagnosis points there.
- `/tdd` — the red-green fix loop.
- `/review` — the shared two-axis review-and-fix loop.
