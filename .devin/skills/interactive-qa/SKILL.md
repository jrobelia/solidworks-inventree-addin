---
name: interactive-qa
description: Generate a test plan from recently implemented code and walk the human through it step-by-step, filing GitHub issues for any failures. Orients from an open PR when available — reads Closes references from the PR body and tests the PR branch's proposed work before merge. Falls back to closed-issue orientation when no PR is present. Use when ralph or a TDD loop has just implemented a feature and you need human verification before closing the loop, or when the user says "QA this", "let's test this", or "verify what ralph built".
---

# Interactive QA

Verify recently implemented features with a human in the loop. Builds a test plan from what was just built, walks through it one step at a time, and files GitHub issues for failures — ready for `/triage` to promote to `ready-for-agent`.

When a PR is present, Interactive QA orients from that PR: reads `Closes #...` references from the PR body, fetches those issues, and tests the PR branch's proposed work before merge. Failures are classified as PR-blocking or follow-up. Issues close only when the PR is merged. When no PR context is present, Interactive QA falls back to closed-issue orientation.

## Position in the pipeline

`grill-with-docs → to-spec → to-tickets → ralph/tdd → **interactive-qa** → triage → ralph (fixes)`

## 1. Orient and build the test plan

**Read domain docs.** Read `docs/agents/domain.md` if it exists — it points to `CONTEXT.md` and any ADRs to read before exploring the codebase.

**Ask scope first.** Smoke (critical paths only, fast) or full (comprehensive, including edge cases)?

**Detect PR context.** Check whether the user has supplied a PR reference or whether there is an open PR for the current branch:

1. Read the current branch:
   ```bash
   git branch --show-current
   ```
2. If the user supplied a PR reference (e.g. `#42` or a PR URL), fetch that PR: call `get_pull_request` with `owner`, `repo`, and `pull_number`.
3. Otherwise, call `list_pull_requests` with `owner`, `repo`, `state: open`, and `head: <owner>:<branch>` to detect an open PR for the current branch.
   - If one open PR is found, confirm with the user: "Found PR #N: \<title\> — orient this QA session from that PR?"
   - If multiple open PRs are found, list them and ask which one to use.
   - If no open PR is found, fall back to **Closed-issue fallback** below.

`owner` and `repo` come from parsing the remote URL — e.g. `git remote get-url origin` returns `https://github.com/owner/repo.git`.

**PR orientation (when a PR is present):**

1. Read the PR body and extract all `Closes #...`, `Fixes #...`, and `Resolves #...` references.
2. Call `get_issue` for each referenced issue number.

   > **Large result:** `mcp_github_issue_read` (method: `get`) may return a message like `"Large tool result (NKB) written to file. Use the read_file tool to access the content at: <path>"` when an issue body exceeds ~8 KB. This is **not an error** — the content is fully recoverable. Extract the file path from the message, call `read_file` on it starting at line 1 with an end line of 100 or more, and locate the `"body"` key within the first 10–20 lines of the result. Continue the skill normally once the content is retrieved.

3. Use the acceptance criteria from those issues as the test plan source — this is the ground truth for what should work.
4. QA runs against the PR branch's current state. You are testing work proposed for merge, not already-merged work.
   - Confirm the PR branch is checked out: `git branch --show-current`. If the local branch differs from the PR head branch, note this to the user.

Also orient with recent commits on the PR branch:

```bash
git log --oneline -10
```

If the PR references no issues, or the referenced issues have no acceptance criteria, ask the user to describe what to test or fall back to `git diff` scope (see **Check for unlinked code changes** below).

Skip to **Form Test Groups** after this step.

**Closed-issue fallback (when no PR context is present):** Follow [CLOSED-ISSUE-FALLBACK.md](CLOSED-ISSUE-FALLBACK.md) for the full orientation procedure.

**Form Test Groups.** Default is one issue per Test Group. Propose a multi-issue group only when issues share acceptance criteria that genuinely cannot be verified in isolation. Present proposed groupings and wait for approval — format in [TEST-PLAN.md](TEST-PLAN.md).

**Check for unlinked code changes.** After presenting issues (or if there are none), run `git diff` and `git status --short`. If unlinked changes are found, ask whether to include them. Only add diff-based steps if the user says yes.

**Build the test plan.** For each approved Test Group, generate test steps from acceptance criteria. Include edge cases: empty/null inputs, boundary values, error paths. See [CHECKLIST.md](CHECKLIST.md). Present the full plan and wait for approval before executing — format in [TEST-PLAN.md](TEST-PLAN.md).

## 2. Walk through the plan

Present one step at a time within each Test Group:

```
**Group X / Step Y of N — [Action Title]**
Issues: #12
Preconditions: [setup needed, or "none"]
Action: [What to do]
Expected: [What should happen]

Pass, fail, or skip?
```

Interpret free-text responses. Confirm interpretation before moving on:

> "Got it — marking as **fail**. Is that right?"

Use available tools as needed to assist verification: run terminal commands, read files, open browser screenshots — whatever the project type requires.

**Label each Test Group as it completes.** Do not defer to the end of the session.

First, verify the `qa-verified` label exists — call `get_label` with `owner`, `repo`, and `name: "qa-verified"`. Do this once, before the first labeling operation, not before every group.

- **Label found**: proceed normally.
- **Label not found**: note to the user — _"The `qa-verified` label doesn't exist in this repo. Run `/setup-custom-skills` to create it — skipping `qa-verified` labels this session."_ Continue the QA session; omit all `qa-verified` labeling steps.

Then, when all steps in a Test Group are resolved:

- **All steps passed or failed (none skipped)** → apply `qa-verified` to every issue in the group immediately, no prompt.
- **Any steps were skipped** → prompt: "Some steps were skipped — mark issue(s) #N (and #M…) as `qa-verified` anyway?" Apply the label based on the user's answer.

Apply the label to each issue in the group:

Call `update_issue` with `owner`, `repo`, `issue_number`, and a `labels` array that includes `qa-verified` plus all labels already on the issue. Note: `labels` is a full replacement array — fetch the issue first to read its current labels before adding.

## 3. File bugs on failure

When a step fails and confirmed:

1. **Explore the codebase** for domain language in the affected area (check `CONTEXT.md`, `UBIQUITOUS_LANGUAGE.md` if present). Write issues in domain terms — no file paths, line numbers, or module names.

2. **Assess scope.** If the failure has multiple distinct causes that could be fixed independently, file separate issues (blockers first). If it's one broken behavior, file one issue.

3. **Classify the failure (PR context only).** When QA is oriented from a PR, ask the user:
   > "Is this failure PR-blocking (must be fixed on this branch before merge) or a follow-up (file for later, does not block merge)?"
   - **PR-blocking**: the fix must land on the current PR branch before this PR can merge. Note this in the QA metadata.
   - **Follow-up**: file as a future work item. Does not block the merge recommendation for this PR.

4. **File immediately** — do not ask the user to review first.

Check `docs/agents/triage-labels.md` for the label vocabulary. Call `create_issue` using the body template in [BUG-REPORT.md](BUG-REPORT.md). Share the issue URL and move to the next step.

## 4. End of session

`qa-verified` labels are applied per Test Group as each group completes — not deferred to the end of the session. Any groups completed before an abandoned session preserve their progress on the next run.

**Print session summary.** After all Test Groups are resolved, print a closing summary:

```
Session complete.
  Passed:  N steps
  Failed:  N steps (N PR-blocking, N follow-up)  ← include counts only in PR context
  Skipped: N steps
  qa-verified groups: #12, #15/#16
  Failure issues filed: #34 (PR-blocking: …), #35 (follow-up: …)
```

**Offer to merge.**

**PR context:** Do not offer to merge if any PR-blocking failures remain unresolved — warn the user instead:

> Warning: N PR-blocking failure(s) were filed this session (#34). Resolve them on this branch before merging.

Follow-up failures do not block the merge recommendation. When all PR-blocking checks pass, state:

> All PR-blocking checks passed. PR #N is merge-ready. Merge it manually when you're ready — merging will automatically close all referenced issues.

Do not call any merge API. The merge is the human's responsibility.

**No PR context (closed-issue fallback):** Follow [CLOSED-ISSUE-FALLBACK.md](CLOSED-ISSUE-FALLBACK.md) for the merge flow and closing output.

## Reference

- [CHECKLIST.md](CHECKLIST.md) — severity guide, anti-patterns, step quality standards
- [docs/adr/0004-pr-merge-as-issue-acceptance-boundary.md](../../docs/adr/0004-pr-merge-as-issue-acceptance-boundary.md) — why PR merge is the issue closure boundary