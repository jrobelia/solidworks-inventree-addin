---
name: ralph
description: Autonomous issue loop for Windows projects. Fetches open ready-for-agent GitHub issues, implements each with TDD via subagent, reviews, commits, then opens a single PR with Closes references. Use when the user says "run ralph", "work through the issue queue", or wants to batch-implement open issues unattended.
---

# RALPH

Windows-compatible AFK agent. Runs inside Devin CLI/Desktop. Implements every open `ready-for-agent` GitHub issue in one batch — implement → review → commit, one issue at a time, on a short-lived batch branch, then opens a single PR for human review and merge.

All GitHub operations use the `gh` CLI, the repo default. See `docs/agents/issue-tracker.md`.

## Before starting

1. Read `docs/agents/issue-tracker.md`, `docs/agents/triage-labels.md`, and `docs/agents/domain.md`.
2. Capture GitHub identity:
   - Run `git remote get-url origin` and parse `owner/repo` from the URL → store as `GITHUB_REPO`.
   - Run `gh api user --jq .login` to get the authenticated GitHub login → store as `GITHUB_USER`.
3. Capture the **parent branch**: run `git branch --show-current` and store as `PARENT_BRANCH`.
   - If `PARENT_BRANCH` is `main` or `master`, stop immediately and refuse to proceed. Ralph never works directly on `main` or `master`. Ask the user to check out a feature branch first.
4. **Check for PR-blocking bug fix mode** — follow [BUG-FIX-MODE.md](BUG-FIX-MODE.md). Stop there if blocking bugs are found and fixed; otherwise continue to step 5.
5. **Propose batch**:
   1. Fetch the open issue queue:
      ```
      gh issue list --state open --label ready-for-agent --json number,title,body,labels
      ```
   2. Identify **blocked** issues and exclude them from the proposal. An issue is blocked if either:
      - It requires code or infrastructure that another open issue will introduce.
      - Its requirements depend on a decision or API shape that another open issue will establish.

      File overlap alone is not a blocker — treat it as a signal to check for real dependencies. A PRD issue that has open implementation issues linked to it cannot be worked on directly.
   3. From the remaining unblocked issues, select a candidate batch:
      - Prefer issues from the same parent PRD or same feature area.
      - Prefer issues with shared acceptance criteria or a natural shared QA path.
      - Default to roughly 3–5 issues. Stop adding issues when the next one would make QA scope muddy or mix unrelated domains.
   4. Generate a candidate batch branch name: `ralph/YYYY-MM-DD-batch-1` using today's date.
      - Check whether it exists locally: `git branch --list <candidate>`.
      - If it exists, increment the suffix (`-batch-2`, `-batch-3`, etc.) until a free name is found. Store the free name as `PROPOSED_BRANCH`.
   5. Present the batch proposal and **stop until the user responds** — format and response handling in [BATCH-PROPOSAL.md](BATCH-PROPOSAL.md). On approval store issue numbers as `APPROVED_BATCH` and branch name as `BATCH_BRANCH`; stop immediately on rejection.
6. Create the **batch branch** using the approved name:
   - Create and check out the branch from `PARENT_BRANCH`: `git checkout -b <BATCH_BRANCH>`.
7. Check `docs/roadmap.md` if it exists — note the current milestone for context.
8. Check `docs/agents/coding-standards.md` exists. If it does not:
   - Inspect the project (language, test framework, build system).
   - Generate a draft `docs/agents/coding-standards.md` using [coding-standards.template.md](coding-standards.template.md) as the structural guide.
   - Stop and ask the user to review the draft before continuing. Do not implement any issues until the file is confirmed.

## The loop

Maintain a list `BATCH_ISSUES` (starts empty) to track every issue successfully implemented in this batch.

For each issue in `APPROVED_BATCH`, working in priority order (bugs → tracer bullets → polish → refactors; lowest number first within each tier):

1. **Prepare**: read the issue body; fetch the parent PRD if referenced; use `grep` to identify the key files involved. Determine `ISSUE_TYPE`:
   - If the issue title or body contains the word "refactor", or the issue has a `refactor` label: `ISSUE_TYPE: REFACTOR`
   - Otherwise: `ISSUE_TYPE: FEATURE`
2. **Capture** the pre-implement SHA: run `git rev-parse HEAD` and store the result. Every review subagent call for this issue will use it.
3. **Implement**: call `run_subagent` with `profile: subagent_general` and a prompt that includes — in order — the GitHub identity block (`GITHUB_USER` and `GITHUB_REPO`), the `ISSUE_TYPE`, the issue number and title, the full issue body, the parent PRD body (if any), the relevant file snippets you found, and then the full contents of [implement-prompt.md](implement-prompt.md).
4. **Evaluate** the implement subagent's report:
   - `COMPLETE` → proceed to the review loop.
   - `NEEDS_CONTEXT` → re-dispatch the implement subagent once with the missing context supplied. If it returns `NEEDS_CONTEXT` again, treat as `BLOCKED`.
   - `BLOCKED` → comment on the issue explaining why and apply `needs-info`:
     ```
     gh issue comment <ISSUE_NUMBER> --body "<reason>"
     gh issue edit <ISSUE_NUMBER> --add-label needs-info
     ```
     Move on to the next issue.
5. **Review loop**: call `run_subagent` with `profile: subagent_general` and [review-prompt.md](review-prompt.md), passing the GitHub identity block, `ISSUE_TYPE`, issue number, issue title, and the pre-implement SHA.
   - `STATUS: CLEAN` → add issue number to `BATCH_ISSUES`, then continue to next issue.
   - `STATUS: REVISED` + `SEVERITY: MINOR` → add issue number to `BATCH_ISSUES`, then continue. Minor-only findings do not warrant a re-review.
   - `STATUS: REVISED` + `SEVERITY: CRITICAL` or `IMPORTANT` → repeat this step. Maximum 3 review iterations.
   - After 3 iterations without `STATUS: CLEAN` or `SEVERITY: MINOR`:
     ```
     gh issue comment <ISSUE_NUMBER> --body "Stuck in review after 3 iterations."
     gh issue edit <ISSUE_NUMBER> --add-label needs-info
     ```
     Move on to the next issue.

## When the loop ends

1. If `BATCH_ISSUES` is empty, print a summary of blocked issues and stop.
2. **Check for completed parent PRDs**: for each issue in `BATCH_ISSUES`, inspect its body for a `Parent: #N` or `## Parent\n#N` reference. For each unique parent PRD found:
   - Fetch all open issues with bodies:
     ```
     gh issue list --state open --json number,title,body
     ```
   - Filter out issues whose body references that parent number (look for `Parent: #N`, `## Parent\n#N`, or `#N` in a parent context). Exclude issues already in `BATCH_ISSUES`.
   - If no such open children remain, add the PRD number to `CLOSED_PRDS`.
3. **Push the batch branch**: run `git push origin <BATCH_BRANCH>` before opening the PR.
4. **Open or update the PR**:
   - Check for an existing PR from `BATCH_BRANCH` to `PARENT_BRANCH`:
     ```
     gh pr list --state open --head <BATCH_BRANCH> --json number
     ```
   - If none exists, create it:
     ```
     gh pr create --title "ralph: batch YYYY-MM-DD" --body "<Closes lines>" --head <BATCH_BRANCH> --base <PARENT_BRANCH>
     ```
   - If one exists, update its body:
     ```
     gh pr edit <number> --body "<Closes lines>"
     ```
   - The body must include a `Closes #N` line for every issue number in `BATCH_ISSUES`, then a `Closes #N` line for every PRD number in `CLOSED_PRDS`, one per line. Example:
     ```
     Closes #12
     Closes #15
     Closes #17
     Closes #10
     ```
5. **Print handoff**:
   ```
   RALPH HANDOFF
   PR: <PR URL>
   Parent branch: <PARENT_BRANCH>
   Batch branch: <BATCH_BRANCH>
   Issues in batch: #N, #N, #N
   Issues blocked: #N (reason), ...
   Total commits: N

   Next step: run /qa against the PR branch to verify before merging.
   ```

## Rules

- Never commit or work on `main`/`master`. Refuse immediately if the parent branch is `main` or `master`.
- One commit per issue from the implement subagent. Never batch commits.
- Never close an issue directly. Issues close only when the PR is merged. See [docs/adr/0004-pr-merge-as-issue-acceptance-boundary.md](../../docs/adr/0004-pr-merge-as-issue-acceptance-boundary.md).
- If the implement subagent reports the build is broken after its commit, run `git revert HEAD --no-edit` before continuing.
- For every GitHub operation, prefer `gh` commands. Only fall back to `mcp_call_tool` if `gh` is unavailable.
