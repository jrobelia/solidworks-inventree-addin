---
name: git
description: Project-specific git guidance for the SolidWorks InvenTree add-in. Reach for this when the default Git rules need project-level detail — Windows commit messages, push rules for open PRs, and code-review diff bases.
---

# Git — project guide

Use this guide for every git operation in this repo. It captures the Windows/PowerShell quirks, push expectations, and code-review base rules that are easy to get wrong.

## Command form

Run git commands from the repository directory as plain `git <command>`.

- Use the `exec` tool's `workdir` parameter to set the working directory for each command. Set it to the repository directory (or a subdirectory within it).
- Do not rely on `cd` persisting across separate `exec` calls; the environment resets the shell to the open project before each call.
- Do not use `git -C <directory> <command>`. The `-C` flag changes git's working tree, and the auto-approve rules in this environment do not match that pattern, so every `git -C` command will require manual approval.

## Default branch

This repo uses a milestone release branch as the integration target (e.g., `milestone-3` at the time of writing). Use the active milestone branch in place of `main` when checking out, pulling, or cleaning up after a merge. If you are unsure of the current target branch, run `gh pr view <number> --json baseRefName` for the open PR.

## Closing issues from milestone branches

GitHub only auto-closes an issue when a commit or PR is merged into the **default** branch (usually `main`). When a PR targets a milestone branch, keywords like `Closes #N` or `Fixes #N` in the PR body or commit message will **not** auto-close the issue when the PR merges. To keep issue tracking clean, manually close the issue or add the `qa-verified`/`done` label when the PR lands in its milestone branch, and rely on the eventual `main` merge only for the final close if needed.

## Before committing

1. Run `git status --short` and `git diff` to see what is changing.
2. Stage only files that belong to the current task. If unrelated changes are present, commit them separately or leave them unstaged.
3. Match the commit style from `git log --oneline -15`.

## Writing commit messages on Windows

PowerShell does not support bash heredoc for `git commit -m`.

### Short commits (default)

Use multiple `-m` flags. This keeps the command readable and avoids temp-file cleanup:

```powershell
git commit -m "fix(task-pane): short title" -m "Longer body." -m "Closes #47"
```

### Long messages

If the message is long, has backticks, or would make the command hard to read, write it to a temp file and commit from that:

```powershell
$msg = "fix(task-pane): short title`n`nLonger body.`n`nCloses #47"
$msg | Out-File -FilePath .gitmessage -Encoding utf8
git commit -F .gitmessage
Remove-Item .gitmessage
```

## What goes into a commit

- One clear reason to exist per commit.
- Reference the issue in the body with `Closes #<number>`, `Fixes #<number>`, or `Relates to #<number>` when applicable.
- Leave git config untouched and avoid interactive git flags like `-i`.
- If `git status` is empty, stop — there is nothing to commit.

## Pushing

Push after the work is committed, the build passes, and any requested review is done.

- If the current branch already has an **open PR**, pushing the completed work is the normal next step to update the PR.
- If the branch has **no open PR** or this is the **first push**, stop and ask the user whether to create a PR or push.
- Confirm with the user before destructive operations: force-push, history rewrite, branch deletion, or checking out over uncommitted changes.

## Merging a PR

Once a PR is reviewed and QA-verified, merge it with a merge commit to match the repo's existing history:

```powershell
gh pr merge <number> --merge --delete-branch
```

- Prefer `--merge` (create a merge commit). Use `--squash` or `--rebase` only when the user explicitly asks for it.
- `--delete-branch` prunes the source branch after merge, consistent with the branch-hygiene guidance below.
- If `gh pr merge` cannot be used non-interactively, use the API instead:
  ```powershell
  gh api -X PUT /repos/<owner>/<repo>/pulls/<number>/merge -f merge_method=merge
  ```
- For PRs targeting a milestone branch, `Closes #N` keywords in commits/PR body will **not** auto-close the issue. Apply `qa-verified`/`done` labels and manually close the child issues after the PR lands.

## Branch hygiene

Don't reuse a branch that has already been merged. Merged branches should be pruned and new work should start from an up-to-date `main`.

### Check whether the current branch is already merged

Use `gh` to check for a merged PR on the current branch — this is more reliable than checking `git branch --merged main` when local `main` is behind:

```powershell
$branch = git branch --show-current
gh pr list --state merged --head "$branch" --json number,title,state
```

If a merged PR is found, the branch has already served its purpose.

### Clean up and start from main

If the current branch is already merged:

```powershell
git checkout main
git pull
git branch -d <stale-branch>
git push origin --delete <stale-branch>
```

Then create a fresh branch for the new task:

```powershell
git checkout -b devin/<issue-or-task-slug>
```

### After a PR is merged

Once the PR for a branch is merged, prune the branch before starting the next task:

- Delete the local branch: `git branch -d <branch>`
- Delete the remote branch: `git push origin --delete <branch>`
- Pull `main` so the next branch starts from the latest state: `git pull`

## Code-review diff base

When another skill asks you to run `/code-review`, use the commit *before the work you are reviewing* as the base. Use `origin/main` only when the whole branch is the review target.

1. Run `git log --oneline` and identify the last commit that is not part of your change.
2. Use that commit as the fixed point:
   ```bash
   git diff <commit-before-your-work>...HEAD
   ```
3. This keeps the review focused on your change, especially on a long PR branch.

Example: if your fix is at `cbdde31` and the previous commit is `d9b36b3`, use `d9b36b3` as the base.
