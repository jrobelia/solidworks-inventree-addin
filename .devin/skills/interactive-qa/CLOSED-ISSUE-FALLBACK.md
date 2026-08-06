# Closed-Issue Fallback

Used when no PR context is detected. Covers both orientation (section 1) and end-of-session handling (section 4).

## Orientation

Query all closed issues and filter out any already carrying `qa-verified`:

Call `list_issues` with `owner` and `repo`, and `state: CLOSED`. Read each returned issue and filter out any whose `labels` array contains `qa-verified`. Do not apply a hard result limit.

- If no untested issues remain, tell the user: "No untested closed issues found — the queue is clear." Then check for git diff scope.
- If the list is large (more than ~20 issues), surface the count: "Found N untested closed issues — do you want to narrow scope before building the test plan?"

Also orient with recent commits:

```bash
git log --oneline -10
```

For ralph: one commit per issue on a long-lived feature branch. For Sandcastle: merge commits per issue branch (`sandcastle/issue-N-slug`) — the merge log tells you which issues were just landed.

Read each untested closed issue to extract the original acceptance criteria — that's your ground truth for what should work.

## End-of-session merge flow

Read the current branch:

```bash
git branch --show-current
```

- If the result is `main` or `master`, skip the merge prompt entirely.
- Otherwise, prompt the user:

> All steps complete. Do you want to merge `<branch>` into `main`?
> - **Yes** — run the merge now and confirm.
> - **No** — leave the branch as-is (e.g. failures need to be fixed first).
> - **Skip** — don't ask again this session.

**If any failure issues were filed during the session**, warn before proceeding:

> Warning: N failure issue(s) were filed this session (#34, #35). Merge anyway?

If the user confirms, perform a standard merge with no force flags:

```bash
git checkout main
git merge <branch>
```

If the merge succeeds, confirm: `Merged <branch> into main successfully.`

If the user declines or skips, exit cleanly and show open failure issues so nothing is forgotten:

```
QA complete — N passed, N failed, N skipped.

Test Groups labeled qa-verified: #12, #15, #16, #18
Filed issues:
- #123 bug: <title>

Run /triage when you're ready to prepare these for ralph.
```
