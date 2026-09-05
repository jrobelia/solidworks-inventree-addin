# Code-review environment notes

## Why an independent reviewer matters

The agent that wrote the code is the wrong agent to review it. Manual reviews by the authoring agent rarely catch the same assumptions and shortcuts that produced the code. Every code review must be run by an independent reviewer — a subagent if you are working locally, or a child session on Devin Cloud. That separation is what makes the findings worth acting on.

## Default path: tool-enabled custom subagents

`/build` runs the two-axis review using the custom subagent profiles `code-review-standards` and `code-review-spec` under `.devin/agents/`. Those profiles have `read`/`grep`/`glob`/`exec` tool access, so the parent only passes `PRE_BUILD_SHA` and the spec. The subagents fetch the diff and commit list and read `docs/agents/coding-standards.md` themselves.

For this to work, the active Devin config must allow:

- `Exec(git diff)`
- `Exec(git log)`
- `Read(**)`

The full invocation is in `build/REFERENCE.md`.

## Fallback path

If the custom subagents are missing, fail, or a tool call is denied, fall back to the `/code-review` skill or `subagent_general` in the foreground. In that case the parent must paste the diff, commit list, standards, and spec.

## Diff base

The diff base should usually be `origin/main`, not the local `main` branch. The local `main` can lag behind `origin/main` and pull in unrelated skill-setup commits.

## Do not edit the skill file

Do not edit `.agents/skills/code-review/SKILL.md` to change this; that file is managed by the skill store and may be overwritten on skill updates. Keep project-level notes here and in `build/REFERENCE.md`.

## Build-skill note

When `/build` runs the two-axis review, that step is part of the workflow, not optional. Use the custom-profile background path when the profiles and permissions are in place; otherwise use the fallback above and the appropriate diff base, then review the work before declaring it done.
