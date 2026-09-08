# Pull request conventions

This doc owns the branch, PR body, and PR lifecycle conventions that `/build`, `/fix`, `/build-afk`, and `/qa` share. Skills should point here rather than repeat the rules.

## Branch names

- Single build ticket: `build/issue-<number>`
- Batch build (parent + children, one branch for all): `build/spec-<parent>-<child>-<child>-...` (e.g. `build/spec-44-45-46-47`)
  - The first number is the parent spec; the rest are the child tickets in frontier order.
- Chained build-afk batch (one branch per child): `build/spec-<parent>-<child>` per child
  - Each child PR targets the previous child branch; the first targets `PARENT_BRANCH`.
- Fix: `fix/issue-<number>`
- General agent work (git skill fallback): `devin/<issue-or-task-slug>`
- If a name already exists, append or increment a trailing `-<N>` suffix until free.

The branch is always cut from the branch the skill was invoked on (the `PARENT_BRANCH` in `/build` and `/fix`), or from the previous child branch in a chained `build-afk` run.

## When to open a PR vs. update one

- If the current branch already has an **open PR** and the work is related to that PR, push and update the existing PR body.
- If the branch has **no open PR**, or the work is unrelated to an existing PR, push and open a new draft PR.
- Always open PRs as **draft** so `/qa` can take them out of draft after verification.

## PR body

Open a draft PR with `gh pr create --draft --base <target>`, or update an existing PR with `gh pr edit <number>`.

A PR body contains:

- `Closes #<ticket>` for each child ticket or bug issue; `Part of #<parent>` to reference a parent spec without closing it.
- For a `/fix` PR, a one-line root cause and the regression test added.
- Acceptance criteria copied from the ticket(s).
- Build and test commands that were run.
- Changed GUI flows and edge cases.
- `### Review notes` — paste `/review`'s `REVIEW_NOTES` here, including any deferred or escalated findings.
- `### Deferred and follow-up issues` — list YELLOW findings intentionally deferred with the user's explicit reason, and RED findings converted into follow-up issues with their issue numbers.
- The `/qa` handoff line at the end:

  `Run /qa on this branch. /qa will take the PR out of draft if QA passes and ask whether to merge.`

## Milestone branch auto-close

GitHub only auto-closes an issue when a commit or PR is merged into the **default** branch (usually `main`). When a PR targets a milestone branch like `milestone-3`, `Closes #N` or `Fixes #N` in the PR body or commit message will **not** auto-close the issue. After the PR lands, manually close the issue or add the `qa-verified`/`done` label.
