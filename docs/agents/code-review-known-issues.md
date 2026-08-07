# Code-review skill — known environment issues

The `code-review` skill spawns two parallel subagents (Standards and Spec). In this Devin CLI environment, background `subagent_general` instances do not have `exec` or `read` permission, so they cannot:

- run `git diff` / `git log`
- read repo files such as `docs/agents/coding-standards.md`
- run `gh issue view ...`

## Workaround

Run both subagents in the **foreground** (`is_background=false`) using `run_subagent` with the `subagent_general` profile. Because they cannot run in parallel, run them one after another:

1. Standards subagent first, with the diff command and `docs/agents/coding-standards.md`.
2. Spec subagent second, with the diff command and the relevant issue numbers (`gh issue view ...`).

Do **not** edit `.agents/skills/code-review/SKILL.md` to change this; that file is managed by the skill store and may be overwritten on skill updates. Keep project-level notes here and in `AGENTS.md`.

## Another gotcha

The diff base for this repo should usually be `origin/main`, not the local `main` branch. The local `main` can lag behind `origin/main` and pull in unrelated skill-setup commits.
