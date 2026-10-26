---
name: afk-loop
description: "Prepare the local SolidWorks add-in AFK ticket loop in this agent session, then emit the approved worker launch command for the user's terminal. Full CLI workers perform checks and independent reviews with resumable evidence and a human SolidWorks QA handoff; do not start workers from this session."
disable-model-invocation: true
triggers: ["user"]
argument-hint: "<issue numbers> | --resume <run>"
---

# AFK loop

One user-facing skill owns the launcher, driver, internal prompts, contracts and isolated regression tests. Full CLI sessions do each phase; no subagent orchestration is required. A successful automated run is ready for SolidWorks QA, never qa-verified.

## 1. Resolve and prepare

Resolve a finite explicit issue list and confirmed base branch. Read this repo's AGENTS.md, tracker conventions and PR conventions. Default to one branch/PR per independent ticket; open blockers stay blocked. No automatic frontier, wave merge, prior-work adoption or branch cleanup is implemented.

Pre-flight at the canonical checkout before preparation:

- `git status --porcelain` returns empty. A dirty tree blocks prepare; commit or preserve the work first.
- `git rev-parse --verify <base>^{commit}` resolves.
- `git branch --show-current` names the base. Execution later requires the canonical checkout on the pinned base, so surface a mismatch now.

Run preparation yourself in this agent session, with the tool's working directory set to the canonical checkout:

```bash
bash .devin/skills/afk-loop/afk-loop.sh --prepare --tickets <N> [<M>] --base milestone-3
```

Preparation queries the tracker read-only and writes `.scratch/afk-loop/<run>/APPROVAL.json`. It pins the actual base and complete issue/parent/blocker snapshots. This mode does not launch Devin workers or require Devin terminal authentication. Read the returned run id and approval file directly; inspect every snapshot and prior-work finding. Keep the user here for playback and approval rather than asking them to run preparation or copy a run id.

If GitHub authentication is unavailable, report that specific blocker and ask the user to authenticate GitHub CLI; then retry preparation here. Preserve dirty-tree and base checks. Finish any authorized tracked workflow changes before preparation so the approval pins the final clean base. Do not claim an existing branch, PR or merged implementation is absent merely because one name is missing.

Prior-work findings remain blockers until resolved. A maintainer may explicitly approve an exact, documentation-only ADR commit as a design reference rather than an implementation. Record its full commit id and reason in that ticket's `prior_work_references` list; preserve the raw `prior_work` findings. The driver verifies that the commit only adds or modifies regular `docs/adr/*.md` files. This does not exempt other commits, merge commits, executable Markdown, branches, or PRs. See contracts.md for the entry shape.

**Done when:** preparation succeeded here, the approval file is inspected, and the finite set, base, blockers, prior work, full bodies/comments and referenced decisions are resolved.

## 2. Confirm the delivery contract

Read contracts.md. Use the existing spec-playback skill to present what each ticket makes Part Sync, BOM Compare, Settings or another named workflow do. Present each seam declaration and any unsettled decisions. For each acceptance criterion, prepare its id, exact obligation and evidence kind: automated, visual, host, or confirmed not-applicable.

Hold for confirmation. Then populate the existing approval file using the read/write tools, with the confirmed seam and full criteria mapping. Set confirmed only after the user's yes. Preserve snapshot hashes and base fields. Host rows remain human checks, not defects to repair.

Publication and assignment are separate opt-ins. Keep allow_publish and allow_claim false unless the user explicitly authorizes those effects for this run. No permission to merge, close issues, label QA, discard work, or upload artifacts is implied.

**Done when:** playback, seams, acceptance mapping and consequential rulings are confirmed and recorded.

## 3. Emit the approved launch command

Emit for the user's own PowerShell terminal; do not launch it from this session:

```powershell
.\.devin\skills\afk-loop\afk-loop.ps1 --run <run>
```

Only when separately approved, append --claim and/or --publish. The default has no remote writes.

The session environment approximates the user's terminal but cannot see profile- or session-scoped variables, so always lead the launch block with `Remove-Item Env:DEVIN_API_KEY -ErrorAction SilentlyContinue` — a no-op when the variable is absent, and the authentication guard then passes on the first attempt. Never print or collect the variable's value.

An unverified launch runs the policy probe first: a separate worktree and neutral CLI session checks scoped writes, single-command execution, guard denials and exact session attribution without implementing a ticket, and its protected hook evidence earns policy_verified. Missing policy evidence blocks execution. To prove policy ahead of launch, or to re-prove it after this package changes, emit `.\.devin\skills\afk-loop\afk-loop.ps1 --smoke <run>` instead. Authentication failures require user action in that terminal, not inspection or copying of credentials.

The run then performs a canonical baseline, then implementation, deterministic checks, two independent review axes, adjudication, bounded repair, evidence verification, and optional publication. The host DLL is not rebuilt or reloaded.

Worker stdout streams into the terminal during each dispatch; the repeated CLI greeting is filtered from the terminal but kept in stream.log. Gate command output stays in its pinned logs; the terminal carries the run preamble, per-attempt verdict lines, gate/publication framing, labeled heartbeats and handoffs. Tell the user to leave the window running and to share the outcome; STATUS.json and PROGRESS.md carry it.

The driver owns commands and state; workers own code and judgment. Workers read existing shared rules but do not call the interactive build/review skills. Architectural or spec ambiguity returns a human handoff. Package implementation approval is not approval to launch a pilot ticket.

**Done when:** the user has the exact command and understands its effects. Stop; the user launches it.

## Watching and resuming

- STATUS.json holds machine-readable transitions, pinned commits, publication, claims, CI, reports and exact session ids.
- PROGRESS.md appends timestamps, dispatches, verdicts, gate/publication framing, heartbeat/stale signals and terminal handoffs.
- Each ticket has immutable per-attempt prompt, job, stream, transcript, report and evidence paths.
- Resume with `afk-loop.ps1 --resume <run>` and the original non-default budgets and effect switches. Drift, missing evidence and unrelated worktree changes block replay; an interrupted policy probe replays as a fresh attempt.
- Approval hashing excludes only the machine-owned `policy_verified` flag; actual policy evidence is still required. Successful permission verification is not a new human approval. Do not ask the user to edit that flag or checkpoint hashes to resume.
- A rate-limited exit (ticket and run status `rate-limited`, exit code 3) is an infrastructure pause, not a verdict: the advertised quota window outlasted the wait cap. Resume after the window; partial work and session ids are retained.
- A blocked run preserves its branches, worktrees and evidence. Route its named choice to /build-hitl; do not discard or silently restart it. Infrastructure pauses — rate limits, exhausted retries, a failed push — name a `--resume` in PROGRESS.md instead of the /build-hitl handoff.
- A ready-for-qa result hands off to the existing /qa workflow. Draft promotion, QA labeling and merge remain human steps.

## Prerequisites and options

Windows with Git for Windows, Python 3.9+, dotnet SDK/net48 targeting support, discoverable SolidWorks interop references, authenticated gh and local Devin CLI. Git Bash is required, not WSL. The PowerShell launcher resolves the bundled devin.exe; no Docker or dev server is involved.

DEVIN_API_KEY can override terminal login; if the CLI reports that conflict, the user resolves it in their own terminal. Do not print or collect its value. Existing personal/organization policy may prevent unattended execution; report the blocker rather than weakening it.

- --rounds N: full review rounds, default 3.
- --verify-rounds N: post-verification repair-confirm cycles, default 1; zero blocks on findings.
- --retries N: extra verdictless infrastructure attempts, default 3.
- Model defaults and environment overrides: see Model selection below.
- AFK_TIMEOUT, AFK_HEARTBEAT, AFK_STALE: positive seconds; defaults 7200, 300, 900.
- AFK_RETRY_DELAYS: non-empty JSON array of non-negative seconds; default [45,120,300].
- AFK_RATE_LIMIT_MARGIN, AFK_RATE_LIMIT_FLOOR, AFK_RATE_LIMIT_MAX_WAIT: non-negative seconds added past the advertised reset, a minimum wait, and the accumulated-wait cap before the run exits rate-limited; defaults 300, 60, 10800.
- DEVIN: explicit CLI executable; AFK_PYTHON: Bash entry-point interpreter override.
- AFK_GIT, AFK_GH, AFK_DOTNET, AFK_DEVIN: JSON command arrays for regression adapters. Never silently substitute these in a real launch.

## Model selection

The driver passes an explicit `--model` on every worker dispatch, including resumed sessions. Defaults reserve Max for Spec review and escalated repairs; these are reasoning-level choices, not a pricing or quota promise.

| Worker phase | Default model | Override |
| --- | --- | --- |
| Policy probe | `swe-2-medium` | `MODEL_POLICY`, then `MODEL_IMPL` |
| Implementation | `swe-2-high` | `MODEL_IMPL` |
| Spec review | `swe-2-max` | `MODEL_REVIEW_SPEC`, then `MODEL_REVIEW` |
| Standards review | `swe-2-high` | `MODEL_REVIEW_STANDARDS`, then `MODEL_REVIEW` |
| Adjudication | `swe-2-high` | `MODEL_ADJUDICATE`, then `MODEL_REVIEW` |
| Regular repair | `swe-2-high` | `MODEL_IMPL` |
| Last allowed regular repair, or any post-verification repair | `swe-2-max` | `MODEL_REPAIR_FINAL` |
| Evidence verification | `swe-2-high` | `MODEL_VERIFY`, then `MODEL_IMPL` |

The first non-empty override in a row wins. Existing `MODEL_IMPL` and `MODEL_REVIEW` group overrides remain supported; a phase-specific override takes precedence. Recovery retries and result corrections retain the phase's model selection. Review findings alone do not escalate the reviewer models.

Tests, formatting, and PR-body generation run as scripts without a model session. Workers do not dispatch custom reviewer profiles or nested agents.

## Package verification

```powershell
python -m pytest ".devin/skills/afk-loop/tests" -n 8 --reruns 1 -q   # ~7 min; needs pip install -r tests/requirements.txt
python -m unittest discover -s ".devin/skills/afk-loop/tests" -p "test_*.py" -v   # sequential fallback, ~26 min
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers
```

The Python harness uses disposable Git repositories, stub external commands, and no credentials. It never runs real workers, pushes, PR creation or tracker edits. Guard and driver tests are not a substitute for the actual user-terminal policy probe or pilot. Do not run the suite while an AFK loop or a manual checkout may move the canonical HEAD — it reads and hashes package files mid-run.

Use targeted tests (`-k <name>`) or `AFK_TEST_FAST=1` while iterating; the latter skips slow end-to-end lifecycle cases and retains gate-blocker and guard tests. Instructions-only edits need `git diff --check` and the relevant focused tests, not the full lifecycle suite.

Changes to the runner, guards, launchers, worker prompts, persisted-state formats, model selection, or test harness need one full sweep without `AFK_TEST_FAST` before commit. A Markdown worker prompt is executable behavior for this rule. Do not repeat a completed full sweep solely for subsequent explanatory-text edits.
