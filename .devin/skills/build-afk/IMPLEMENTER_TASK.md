# /build-afk implementer task template

Dispatch one per ticket. Fill every `{{slot}}`; the implementer profile body already carries the loop, the seam discipline, and the status contract — this template carries the per-ticket specifics and the pointers.

`{{extra_context}}` carries the outcomes of the tickets this one was blocked on plus any spec rulings confirmed at the batch gate that govern this ticket — write rulings as settled spec, not as open questions. Write `None.` when empty.

```
run_subagent(
  profile: "build-implementer",     # rounds 4-5 of a fix ladder: "build-implementer-max"
  is_background: false,             # serial foreground by default; background within REFERENCE.md's concurrency caps once tool grants exist
  title: "Ticket {{ticket}}: {{ticket_title}}",
  task: <this file, slots filled>
)
```

---

You are implementing ticket #{{ticket}}: {{ticket_title}}

## Ticket

Fetch the body and **all comments** first — comments are part of the spec (`docs/agents/issue-tracker.md` `## Comments are part of the spec`):

```
gh issue view {{ticket}} --json title,body,comments,labels
```

{{extra_context}}

## Confirmed seam

Read the seam note before designing anything: `{{seam_note_path}}`. It is the approved design — build inside it. A seam that proves wrong in contact with the code is a `BLOCKED`/`ambiguity` with the candidate seams, not a silent redesign.

## Worktree

Work inside `{{worktree_path}}` — your own git worktree under `.worktrees/`, on branch `{{branch}}` (`afk/<ticket>` in a batch run, `build/issue-<N>` in a queue run), cut from base SHA `{{base_sha}}`. Verify before starting:

```
git -C {{worktree_path}} rev-parse HEAD            # must print {{base_sha}}
git -C {{worktree_path}} branch --show-current     # must print {{branch}}
```

Every file edit and every command runs inside the worktree, never the main checkout.

## Context pointers

Reach each only when its branch fires:

- `docs/agents/coding-standards.md` — before writing any C#. `## Module Design` governs the seam; `## Build & Test Commands` owns the commands below.
- `CONTEXT.md` — before writing anything a user will see: identifiers, status strings, commit text. Domain terms: IPN, Fetch, Apply, Push, Task Pane.
- `.devin/skills/solidworks-inventree-testing/SKILL.md` — when the diff touches `SwInventreeAddin/UI/`, any `*ViewModel*.cs`, any `*.xaml`, or a dialog/window class: run the WPF smoke harness before committing.

Skills cannot be invoked from inside a subagent — every pointer above is a file to read.

## Tool reality (smoke-tested)

Your granted toolset is `read`, `edit`, `exec`, `grep`, `glob` (shown in your function list as `find_file_by_name`) — nothing else. Custom profiles are capped at these five in this build: `allowed-tools` can narrow the set but never widen it — verified by dispatch with no `allowed-tools` declared at all. Only the built-in `subagent_general` gets the full toolset, and it always runs on the parent's model.

- No `write` tool: create new files through `exec` (`cat > path <<'EOF'` or `git apply`). `edit` fails on files that do not exist.
- No background-shell tools (`get_output`, `kill_shell`, `write_to_process`): every command is a foreground `exec` that blocks until exit — run commands that terminate.
- Running in the background means unapproved tool calls are auto-denied — the orchestrator pre-grants `exec` before dispatching you there.

## Loop

1. Orient: the ticket, the seam note, `git -C {{worktree_path}} log -n 5 --oneline`.
2. Red: one failing test at the confirmed seam. Run it and confirm it fails before writing production code — a test that passes immediately is testing existing behaviour.
3. Green: the smallest change that passes. Refactor after green.
4. Feedback loops — all green before the commit:
   - `dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers` from the worktree root.
   - `dotnet format` on your changed C# files:

     ```bash
     mapfile -t files < <( { git diff --name-only --diff-filter=AM HEAD; git ls-files --others --exclude-standard; } | grep '\.cs$' )
     if ((${#files[@]})); then dotnet format "Solidworks Inventree Add-In.sln" --include "${files[@]}"; fi
     ```

   - The WPF smoke harness when the UI pointer fired.

   Any loop red: fix it and re-run, or return `BLOCKED` — never commit on red.
5. Self-review: read your own `git diff` in the worktree — completeness against every acceptance criterion, drift outside the confirmed seam, leftovers (commented code, TODOs, stray files).
6. One logical commit referencing the ticket and parent spec:

   ```
   git add -A && git commit -m "<summary> (#{{ticket}}{{parent_ref}})"
   ```

   `{{parent_ref}}` is `, part of #<spec>` in a batch run, empty in a queue run. Never push, never open a PR — the orchestrator owns the merge, the push, and the PR. If you are resumed for a fix round after your branch already merged into the batch branch, the fix lands as a new commit — never amend or rebase the merged tip.

## Report

Write the full report to `{{report_path}}`: what you implemented, the TDD evidence (the red command and its failing output, the green command and its passing output), files changed, self-review findings, concerns.

Then return only this JSON — no prose around it:

```json
{
  "status": "COMPLETE | COMPLETE_WITH_CONCERNS | BLOCKED",
  "branch": "{{branch}}",
  "commit": "<short sha>",
  "worktree_path": "{{worktree_path}}",
  "test_summary": "<one-line result>",
  "report_path": "{{report_path}}",
  "concerns": ["<specific doubt — when COMPLETE_WITH_CONCERNS>"],
  "blocked_kind": "<context | capability | size | ambiguity — required when BLOCKED>",
  "reason": "<empty when COMPLETE; what unblocks the ticket when BLOCKED>"
}
```

`BLOCKED` specifics go in `reason` — the orchestrator acts on it directly. Still commit and push nothing: leave partial work uncommitted in the worktree and say so in `reason`.
