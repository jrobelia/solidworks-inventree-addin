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

## Before committing

1. Run `git status --short` and `git diff` to see what is changing.
2. Stage only files that belong to the current task. If unrelated changes are present, commit them separately or leave them unstaged.
3. Match the commit style from `git log --oneline -15`.

## Writing commit messages on Windows

PowerShell does not support bash heredoc for `git commit -m`. Use one of these patterns:

```powershell
# Multiple -m flags
git commit -m "fix(task-pane): short title" -m "Longer body." -m "Closes #47"

# Or a temporary file for longer messages
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

## Code-review diff base

When another skill asks you to run `/code-review`, use the commit *before the work you are reviewing* as the base. Use `origin/main` only when the whole branch is the review target.

1. Run `git log --oneline` and identify the last commit that is not part of your change.
2. Use that commit as the fixed point:
   ```bash
   git diff <commit-before-your-work>...HEAD
   ```
3. This keeps the review focused on your change, especially on a long PR branch.

Example: if your fix is at `cbdde31` and the previous commit is `d9b36b3`, use `d9b36b3` as the base.
