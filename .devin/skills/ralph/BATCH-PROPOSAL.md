# Batch Proposal Format and Response Handling

Used by "Before starting" step 5. Present this block and stop until the user responds.

## Output format

```
RALPH BATCH PROPOSAL
Batch branch: <PROPOSED_BRANCH>
Parent branch: <PARENT_BRANCH>
PR target: <PARENT_BRANCH>

Proposed issues:
- #N: <title>
- #N: <title>

Rationale: <why these issues belong together — same parent PRD, same feature area, shared acceptance criteria, or natural shared QA path>

Excluded ready issues (if any):
- #N: <title> — <reason excluded>

Reply with:
  "approve"      — accept this batch as-is
  "reject"       — cancel
  modifications  — list issues to add or remove, or specify a new branch name
```

## Response handling

- **"approve"**: store the proposed issue numbers as `APPROVED_BATCH` and `PROPOSED_BRANCH` as `BATCH_BRANCH`. Return to SKILL.md step 6.
- **"reject"**: stop immediately.
- **modifications**: apply the user's changes (add/remove issues, rename branch), re-present the updated proposal, and wait again. Repeat until the user approves or rejects.
