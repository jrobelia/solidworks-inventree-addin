---
name: qa
description: Verify the current branch through the SolidWorks InvenTree Add-In GUI.
disable-model-invocation: true
---

# QA

Human-in-the-loop verification for the SolidWorks InvenTree Add-In. QA sits after `/build` and before `/triage`:

`grill-with-docs → to-spec → to-tickets → build → qa → triage`

QA orients from the current branch, proposes Test Groups, builds a GUI-focused test plan, runs the preflight, walks the user through each step, labels verified issues, and files failures for triage.

## References

- [TEST-PLAN.md](TEST-PLAN.md) — group and test plan templates
- [BUG-REPORT.md](BUG-REPORT.md) — failure issue body template
- [PREFLIGHT.md](PREFLIGHT.md) — SolidWorks/build/test/registration preflight
- [CHECKLIST.md](CHECKLIST.md) — severity, step quality, edge cases, anti-patterns
- `docs/agents/domain.md` and `CONTEXT.md` — domain vocabulary
- `docs/agents/issue-tracker.md` — `gh` CLI conventions
- `docs/agents/triage-labels.md` — label vocabulary

## Scope

QA is interactive GUI verification. Every step is a user action in the SolidWorks InvenTree Add-In and an observable result. The agent asks the user to perform and observe. All questions stay in the GUI and domain language; the agent does not ask the user to read source files, diffs, or line numbers.

## 1. Orient

Detect the source of truth for this QA pass. Try, in order: open PR for the current branch, closed issues, current diff.

### Open PR for the current branch

1. Read the current branch:
   ```powershell
   git branch --show-current
   ```
2. List open PRs for that branch:
   ```powershell
   gh pr list --state open --json number,title,body,headRefName --head "<branch>"
   ```
3. If one open PR is found, present it and ask: "Found PR #N — orient this QA pass from that PR?"
   - If confirmed, view the PR and extract the issues it closes:
     ```powershell
     gh pr view <number> --json number,title,body,closingIssuesReferences,headRefName
     ```
   - Fetch each referenced issue:
     ```powershell
     gh issue view <number> --json number,title,body,labels,comments
     ```
4. If multiple open PRs are found, list them and ask which to use.
5. If the list is empty, fall back to closed issues.

### Closed issues

List recently closed issues, keeping only those without the `qa-verified` label:

```powershell
gh issue list --state closed --json number,title,body,labels,comments --jq '[.[] | select((.labels | map(.name)) | index("qa-verified") | not)]'
```

If the list is large (more than ~20), surface the count and ask the user to narrow scope. Read each selected issue to extract acceptance criteria.

If the list is empty, fall back to the current diff.

### Current diff

Run `git diff` and `git status --short`. If unlinked changes are present, ask the user whether to include them. Use the diff scope only when the user says yes.

## 2. Propose Test Groups

Default: one issue per Test Group. Propose a multi-issue group only when issues share acceptance criteria that cannot be verified in isolation.

Present the proposed groups using the [TEST-PLAN.md](TEST-PLAN.md) group format. Print the full proposal in the chat response first, then ask the user to reply with approve/edit/merge/split. Do not use `ask_user_question` for long proposal approvals — the question dialog can hide the previous chat and make the proposal hard to review.

## 3. Build the test plan

Read `docs/agents/domain.md` and `CONTEXT.md` first if they exist. Use their vocabulary throughout the plan.

For each approved Test Group, generate test steps from the acceptance criteria. Every step must be a user action in the SolidWorks InvenTree Add-In GUI with an observable result. Use domain terms from `CONTEXT.md` (Task Pane, IPN, InvenTree Part PK, Fetch, Apply, Push, Part Sync, BOM Compare, etc.).

Include at least one edge case per feature area. See [CHECKLIST.md](CHECKLIST.md).

If the change touches the **Task Pane**, a **dialog**, a **control**, or a **data-bound property**, add a GUI functionality group using the categories and example in the GUI functionality testing section of [TEST-PLAN.md](TEST-PLAN.md).

Present the full plan using the [TEST-PLAN.md](TEST-PLAN.md) plan format. Print the full plan in the chat response first, then ask the user to reply with approve/edit/reorder. Do not use `ask_user_question` for long plan approvals — the question dialog can hide the previous chat and make the plan hard to review.

## 4. Preflight

Run the preflight in [PREFLIGHT.md](PREFLIGHT.md) before the GUI test pass. Stop if the build or test run fails and ask the user to fix the branch before QA.

## 5. Walk the steps

Present one step at a time from the approved plan. Print the full step (preconditions, action, and expected result) to the chat panel as the assistant's message first. Only after the step is visible in chat, call `ask_user_question` with a concise prompt:

> Pass, Fail, or Skip?

Never call `ask_user_question` for a step without first printing the step in the chat message. The question body must contain only the short prompt and the three options.

Interpret the answer. If the result is unclear, confirm before moving on:

> Marking as **Fail**. Correct?

### On Fail

1. Ask for the severity: P0, P1, P2, or P3. See [CHECKLIST.md](CHECKLIST.md).
2. If a PR is in context, ask whether the failure is **PR-blocking** or **follow-up**.
3. Explore the codebase only to understand the domain area. Write the failure in domain terms from `CONTEXT.md`; leave out file paths, line numbers, and module names.
4. File the issue immediately using the [BUG-REPORT.md](BUG-REPORT.md) template and `gh issue create`. Apply the labels `bug,needs-triage`.
5. Continue with the next step.

### On Skip

Track skipped steps. When the group completes, ask whether to apply `qa-verified` anyway.

## 6. Label verified groups

When a Test Group completes:

- If any step failed, skip `qa-verified` for that group. The failure issue carries the result.
- If all steps passed and none were skipped, apply `qa-verified` to every issue in the group immediately:
  ```powershell
  gh issue edit <number> --add-label qa-verified
  ```
- If any steps were skipped, ask: "Some steps were skipped — mark issue(s) #N (and #M…) as `qa-verified` anyway?" Apply the label based on the answer.

Apply `qa-verified` as each group completes, not at the end of the session.

## 7. End summary

After all Test Groups are resolved, print a closing summary:

```
Session complete.
  Passed:  N steps
  Failed:  N steps (N PR-blocking, N follow-up)
  Skipped: N steps
  qa-verified groups: #12, #15/#16
  Failure issues filed: #34 (PR-blocking: …), #35 (follow-up: …)
```

QA ends with the summary. The merge is the human's responsibility.
