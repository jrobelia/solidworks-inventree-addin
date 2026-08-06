# PR-Blocking Bug Fix Mode

Entered from "Before starting" step 4 when `PARENT_BRANCH` already has an open PR.

## Steps

1. Check whether `PARENT_BRANCH` has an open PR:
   ```
   gh pr list --state open --head <PARENT_BRANCH> --json number,url,body
   ```
2. If an open PR exists, store its number as `EXISTING_PR_NUMBER`, its URL as `EXISTING_PR_URL`, and its current body as `EXISTING_PR_BODY`.
3. Fetch open issues with both `ready-for-agent` and `bug` labels:
   ```
   gh issue list --state open --label ready-for-agent,bug --json number,title,body
   ```
4. From those issues, identify **PR-blocking bugs**: issues whose body contains `#<EXISTING_PR_NUMBER>` or `<PARENT_BRANCH>`.
5. If any PR-blocking bugs are found, enter **bug fix mode**:
   - Store the matched issue numbers as `BUG_ISSUES`.
   - Work on the current branch (`PARENT_BRANCH`). **Do not create a new branch.**
   - Run the implement → review loop (same structure as "The loop" in SKILL.md) for each issue in `BUG_ISSUES`, lowest number first.
   - After each fix is committed, update the existing PR body:
     ```
     EXISTING_PR_BODY = EXISTING_PR_BODY + "\nCloses #N"
     gh pr edit <EXISTING_PR_NUMBER> --body "<EXISTING_PR_BODY>"
     ```
   - After all fixes are committed and the PR body is updated, print:

     ```
     RALPH BUG FIX HANDOFF
     PR: <EXISTING_PR_URL>
     Branch: <PARENT_BRANCH>
     Bug fixes committed: #N, #N, ...
     PR body updated with Closes references.

     Next step: run /interactive-qa against this PR branch to re-verify before merging.
     ```

   - **Stop.** Do not proceed to batch proposal.
6. If no open PR exists, or an open PR exists but no PR-blocking bugs are identified, return to SKILL.md step 5.
