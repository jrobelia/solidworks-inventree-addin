# /build-afk audit task template

Dispatch one per audit-unit ticket — a ticket whose deliverable is analysis (documents), not a code diff. Fill every `{{slot}}`; the `build-implementer` profile body carries the dispatch contract — this template carries the audit unit's specifics. There is no worktree, branch, or commit: the agent writes declared artifacts to `reports/` on the main checkout.

`{{ticket_body_and_comments}}` carries the full issue body and all comments — paste them, not a fetch the grants may not permit. `{{extra_context}}` carries spec rulings confirmed at the batch gate that govern this ticket — write rulings as settled spec, not as open questions. Write `None.` when empty. `{{reports_dir}}` is the run's `.scratch/build-afk/<run>/reports/` as an absolute path; `{{report_path}}` is where your own report lands inside it.

```
run_subagent(
  profile: "build-implementer",     # the #252 precedent — no dedicated audit profile
  is_background: true,              # needs exec pre-granted for heredoc file creation
  title: "Audit {{ticket}}: {{ticket_title}}",
  task: <this file, slots filled>
)
```

---

You are auditing ticket #{{ticket}}: {{ticket_title}}

## Ticket

The body and comments below are the spec — read all of it; comments are part of the spec (`docs/agents/issue-tracker.md` `## Comments are part of the spec`).

```
{{ticket_body_and_comments}}
```

{{extra_context}}

## Confirmed coverage declaration

Read the seam note first: `{{seam_note_path}}`. Its `## Audit Coverage` declaration is your contract — the coverage items you must examine, the evidence rule every finding must satisfy, the deliverable artifact paths under `{{reports_dir}}`, the tracker side-effects your findings may request, and the Watch items the verification pass will check against your artifacts.

## Deliverables

- Write each artifact the declaration names to its path under `{{reports_dir}}` — create files through `exec` heredoc (`cat > path <<'EOF'`); there is no `write` tool. `test -s` each after writing — a declared artifact that is missing or empty is a failed dispatch.
- `.scratch/` is gitignored — your artifacts live on the main checkout, never in a worktree. Do not run `git` mutation commands; there is nothing to commit.
- Write only the paths the declaration names — an undeclared artifact is scope creep.

## Rules

- Cover every coverage item in the declaration — an item your artifacts leave unaddressed fails verification and the unit comes back to you.
- Every finding cites a resolvable anchor per the declaration's evidence rule — file:line or doc section; a finding without one is invalid.
- **Never run `gh` or any tracker write** (`gh issue comment`, `gh issue create`, label edits). Side-effects are orchestrator-owned and fire only after a verification pass on your persisted artifacts — you request them via `side_effects`, you never execute them.
- Your granted toolset is `read`, `edit`, `exec`, `grep`, `glob` — nothing else. Every `exec` is foreground; run commands that terminate.

## Loop

1. Orient: the ticket, the seam note, every file and doc section the coverage items name.
2. Examine each coverage item in order; gather anchored evidence as you go.
3. Write each declared artifact under `{{reports_dir}}`; confirm each exists non-empty.
4. Self-review: every coverage item addressed, every finding anchored to a location that resolves, no undeclared artifact written, no `gh` call made.

## Report

Write your full report to `{{report_path}}`: the coverage items one by one and where each is addressed, the findings with their anchors, the artifacts written, self-review findings, and concerns.

Then return only this JSON — no prose around it:

```json
{
  "status": "COMPLETE | COMPLETE_WITH_CONCERNS | BLOCKED",
  "side_effects": ["<a side-effect the seam declared and your findings trigger — name the seam's own entries; naming an effect the seam never declared is a contract violation>"],
  "report_path": "{{report_path}}",
  "concerns": ["<specific doubt — when COMPLETE_WITH_CONCERNS>"],
  "blocked_kind": "<context | capability | size | ambiguity — required when BLOCKED>",
  "reason": "<empty when COMPLETE; what unblocks the ticket when BLOCKED>"
}
```

`side_effects` is `[]` when your findings trigger nothing — verification still runs. `BLOCKED` specifics go in `reason` — the orchestrator acts on it directly. Leave your artifacts in place either way; say what is partial in `reason`.
