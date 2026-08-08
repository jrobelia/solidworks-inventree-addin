# Issue tracker: GitHub

Issues and PRDs for this repo live as GitHub issues. Use the `gh` CLI for all operations.

## Conventions

- **Create an issue**: `gh issue create --title "..." --body "..."`. Use a heredoc for multi-line bodies.
- **Read an issue**: `gh issue view <number> --comments`
- **List issues**: `gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'`
- **Comment on an issue**: `gh issue comment <number> --body "..."`
- **Apply / remove labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **Close**: `gh issue close <number> --comment "..."`

Infer the repo from `git remote -v` — `gh` does this automatically when run inside a clone.

## When a skill says "publish to the issue tracker"

Create a GitHub issue.

## When a skill says "fetch the relevant ticket"

Run `gh issue view <number> --comments`.

## Parent and child issues

This repo uses the conventions from `to-tickets`:

- A **child issue** references its parent in a `## Parent` section.
- A child issue lists its blockers in a `## Blocked by` section, or uses the tracker's native blocking / sub-issue links when available.
- To find child issues of parent `#N`, list open issues and filter for bodies containing `## Parent` followed by `#N`.
- To order child issues by dependency, resolve each issue's `## Blocked by` references (or native blocking links). Issues with no blockers come first; otherwise process blockers before the issue that depends on them.
