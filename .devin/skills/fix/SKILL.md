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
2. **Load the context pointers.** Read `docs/agents/coding-standards.md` (`## Module Design` and `## Build & Test Commands`), `docs/agents/pr-conventions.md` (`## Branch names` and `## PR body`), and `CONTEXT.md`/`docs/agents/domain.md` so the fix uses the repo's design vocabulary, branch/PR conventions, and domain language. If the fix will create, change, or remove a public seam, consult `/codebase-design` and state the seam declaration required by `## Module Design` before writing code.
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
   - **No open PR** — verify `git status --short` is clean (stop and ask otherwise), then create a fix branch per `docs/agents/pr-conventions.md` `## Branch names`. `PRE_FIX_SHA` was already captured above. A draft PR opens at step 8.
   **Done when:** the branch is chosen, `PRE_FIX_SHA` is captured, and the working tree is ready for the fix commit.
5. **Fix.** The path depends on how you got here:
   - **Hard bug** — apply the fix and regression test produced by `/diagnosing-bugs`.
   - **Normal bug** — invoke the `/tdd` skill first using the `skill` tool (`command: "invoke"`, `skill: "tdd"`). Write a failing regression test that reproduces the bug, then write the minimal fix that makes it pass. Do not write the fix or its test outside the `/tdd` red-green loop. If the bug cannot be expressed as a failing test, treat it as a hard-bug signal and go back to step 3 to invoke `/diagnosing-bugs`.
   **Done when:** `/tdd` has completed, the regression test passes, and the agent verification command is green.
6. **Commit and verify.** Run the agent verification command per `docs/agents/coding-standards.md` `## Build & Test Commands`, fix failures, then commit referencing `#N`. `/review` measures a committed diff, so the commit must land before the review call.
   **Done when:** the fix is committed and the agent verification command passes on the commit.
7. **Review.** Invoke `/review` with `REVIEW_BASE` = `PRE_FIX_SHA`, `SPEC_SOURCE` = the bug issue body, `AXES` = `both`. `/review` runs an adjudicated two-pass review-and-fix loop internally and returns a final `REVIEW_STATUS` plus the full `REVIEW_NOTES` that record every finding's disposition across all passes. Capture both exactly.
   - If `REVIEW_STATUS` is `clean`, `resolved`, or `deferred`, proceed.
   - If `REVIEW_STATUS` is `escalated`, stop and hand off to the user; the `REVIEW_NOTES` will include the follow-up issue numbers.
   - If `REVIEW_STATUS` is `capped`, stop and ask the user how to proceed.
   **Done when:** `/review` has completed and returned `clean`, `resolved`, or `deferred`, or the user has been consulted on `escalated`/`capped`.
8. **Ship.** Push the branch. Use the final `REVIEW_NOTES` from the completed `/review` loop. On an existing PR, append `Closes #N` and the final `REVIEW_NOTES` to its body with `gh pr edit`; on a new PR, include them when opening it. Paste the notes verbatim under `### Review notes` and any `deferred` or `escalated` items under `### Deferred and follow-up issues`, per `docs/agents/pr-conventions.md` `## PR body`. Do not summarize, paraphrase, or reduce the subagent blocks to verdict lines.
   **Done when:** the branch is pushed and the PR body contains the full final `REVIEW_NOTES`.

## Skills invoked

- `/diagnosing-bugs` — hard-bug diagnosis and fix loop.
- `/improve-codebase-architecture` — user-invoked; hand the seam finding to the user when diagnosis points there.
- `/codebase-design` — when the fix touches a public seam.
- `/tdd` — the red-green fix loop for normal bugs.
- `/review` — the shared two-axis review-and-fix loop.
