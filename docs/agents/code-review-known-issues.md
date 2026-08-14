# Code-review skill — known environment issues

The `/build` workflow runs a two-axis review by default using the custom subagent profiles `code-review-standards` and `code-review-spec` under `.devin/agents/`. Both profiles have `allowed-tools: []`, so they can run in the background without `read`/`exec` permission prompts. The parent `/build` agent pre-computes and pastes the diff, commit list, standards context, and spec contents into each subagent prompt.

## Default path: custom profiles in the background

- **Standards axis:** `run_subagent` with `profile: code-review-standards` in the background. The prompt contains the diff, commit list, `docs/agents/coding-standards.md`, and the Fowler smell baseline.
- **Spec axis:** `run_subagent` with `profile: code-review-spec` in the background. The prompt contains the diff, commit list, and the originating issue/PRD/spec body.
- Both subagents return separate `## Standards` and `## Spec` findings blocks. The parent parses and classifies each finding as RED / YELLOW / GREEN.

## Fallback path: `subagent_general` or `/code-review`

If either profile file is missing, or a custom subagent fails, requests more context, emits a tool-denial error, or returns incomplete output, `/build` falls back to the existing `/code-review` path. In that case:

- Run both axes in the **foreground** (`is_background=false`) using `subagent_general` with the same pasted context, one after another.
- The Standards subagent receives the diff and `docs/agents/coding-standards.md`.
- The Spec subagent receives the diff and the relevant issue/PRD/spec contents.

Do **not** edit `.agents/skills/code-review/SKILL.md` to change this; that file is managed by the skill store and may be overwritten on skill updates. Keep project-level notes here; the root `AGENTS.md` links to this file.

## Diff base

The diff base for this repo should usually be `origin/main`, not the local `main` branch. The local `main` can lag behind `origin/main` and pull in unrelated skill-setup commits.

## Legacy foreground-only workaround (superseded)

The old recommendation to default to foreground `subagent_general` is now legacy. Custom profiles with `allowed-tools: []` and pre-computed context are the default. Only use the foreground fallback when the custom profiles are absent or fail.

## Build-skill note

When `/build` runs the two-axis review, that step is part of the workflow, not optional. Use the custom-profile background path when the profiles exist; otherwise use the fallback above and the appropriate diff base, then review the work before declaring it done.
