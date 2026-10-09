---
name: afk-loop
description: "Prepare and launch the local SolidWorks add-in AFK ticket loop: full CLI sessions, pinned automated checks, independent reviews, resumable evidence, and a human SolidWorks QA handoff. Emit commands for the user's terminal; do not start real workers from this agent session."
disable-model-invocation: true
triggers: ["user"]
argument-hint: "<issue numbers> | --resume <run>"
---

# AFK loop

One user-facing skill owns the launcher, driver, internal prompts, contracts and isolated regression tests. Full CLI sessions do each phase; no subagent orchestration is required. A successful automated run is ready for SolidWorks QA, never qa-verified.

## 1. Resolve and prepare

Resolve a finite explicit issue list and confirmed base branch. Read this repo's AGENTS.md, tracker conventions and PR conventions. Default to one branch/PR per independent ticket; open blockers stay blocked. No automatic frontier, wave merge, prior-work adoption or branch cleanup is implemented.

Emit this command for the user's own PowerShell terminal at the canonical checkout:

```powershell
.\.devin\skills\afk-loop\afk-loop.ps1 --prepare --tickets <N> [<M>] --base milestone-3
```

Git Bash equivalent:

```bash
bash .devin/skills/afk-loop/afk-loop.sh --prepare --tickets <N> --base milestone-3
```

Preparation queries the tracker read-only and writes `.scratch/afk-loop/<run>/APPROVAL.json`. It pins the actual base and complete issue/parent/blocker snapshots. Ask the user to share the run id; inspect its approval file and prior-work findings. Do not claim an existing branch, PR or merged implementation is absent merely because one name is missing.

**Done when:** the finite set, base, blockers, prior work, full bodies/comments and referenced decisions are resolved.

## 2. Confirm the delivery contract

Read contracts.md. Use the existing spec-playback skill to present what each ticket makes Part Sync, BOM Compare, Settings or another named workflow do. Present each seam declaration and any unsettled decisions. For each acceptance criterion, prepare its id, exact obligation and evidence kind: automated, visual, host, or confirmed not-applicable.

Hold for confirmation. Then populate the existing approval file using the read/write tools, with the confirmed seam and full criteria mapping. Set confirmed only after the user's yes. Preserve snapshot hashes and base fields. Host rows remain human checks, not defects to repair.

Publication and assignment are separate opt-ins. Keep allow_publish and allow_claim false unless the user explicitly authorizes those effects for this run. No permission to merge, close issues, label QA, discard work, or upload artifacts is implied.

**Done when:** playback, seams, acceptance mapping and consequential rulings are confirmed and recorded.

## 3. User-terminal policy smoke test

The Windows CLI must demonstrate that it loads and honors the generated worker policy. Emit, do not execute here:

```powershell
.\.devin\skills\afk-loop\afk-loop.ps1 --smoke <run>
```

The probe uses a separate worktree and a neutral CLI session. It checks scoped writes, single-command execution, guard denials and exact session attribution without implementing the ticket. Its protected hook evidence earns policy_verified; missing policy evidence blocks execution. Ask the user to share the result. Authentication failures require user action in that terminal, not inspection or copying of credentials.

**Done when:** the actual CLI probe passes on this machine, not merely the stub tests.

## 4. Emit the approved run command

```powershell
.\.devin\skills\afk-loop\afk-loop.ps1 --run <run>
```

Only when separately approved, append --claim and/or --publish. The default has no remote writes. A run performs a canonical baseline, then implementation, deterministic checks, two independent review axes, adjudication, bounded repair, evidence verification, and optional publication. The host DLL is not rebuilt or reloaded.

The driver owns commands and state; workers own code and judgment. Workers read existing shared rules but do not call the interactive build/review skills. Architectural or spec ambiguity returns a human handoff. Package implementation approval is not approval to launch a pilot ticket.

**Done when:** the user has the exact command and understands its effects. Stop; the user launches it.

## Watching and resuming

- STATUS.json holds machine-readable transitions, pinned commits, publication, claims, CI, reports and exact session ids.
- PROGRESS.md appends timestamps, dispatches, heartbeat/stale signals and terminal handoffs.
- Each ticket has immutable per-attempt prompt, job, stream, transcript, report and evidence paths.
- Resume with `afk-loop.ps1 --resume <run>` and the original non-default budgets and effect switches. Drift, missing evidence and unrelated worktree changes block replay.
- A blocked run preserves its branches, worktrees and evidence. Route its named choice to /build-hitl; do not discard or silently restart it.
- A ready-for-qa result hands off to the existing /qa workflow. Draft promotion, QA labeling and merge remain human steps.

## Prerequisites and options

Windows with Git for Windows, Python 3.9+, dotnet SDK/net48 targeting support, discoverable SolidWorks interop references, authenticated gh and local Devin CLI. Git Bash is required, not WSL. The PowerShell launcher resolves the bundled devin.exe; no Docker or dev server is involved.

DEVIN_API_KEY can override terminal login; if the CLI reports that conflict, the user resolves it in their own terminal. Do not print or collect its value. Existing personal/organization policy may prevent unattended execution; report the blocker rather than weakening it.

- --rounds N: full review rounds, default 3.
- --verify-rounds N: post-verification repair-confirm cycles, default 1; zero blocks on findings.
- --retries N: extra verdictless infrastructure attempts, default 3.
- MODEL_IMPL, MODEL_REVIEW, MODEL_REPAIR_FINAL: CLI model overrides; defaults are swe-2-max, not a pricing promise.
- AFK_TIMEOUT, AFK_HEARTBEAT, AFK_STALE: positive seconds; defaults 7200, 300, 900.
- AFK_RETRY_DELAYS: non-empty JSON array of non-negative seconds; default [45,120,300].
- DEVIN: explicit CLI executable; AFK_PYTHON: Bash entry-point interpreter override.
- AFK_GIT, AFK_GH, AFK_DOTNET, AFK_DEVIN: JSON command arrays for regression adapters. Never silently substitute these in a real launch.

## Package verification

```powershell
python -m unittest discover -s ".devin/skills/afk-loop/tests" -p "test_*.py" -v
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers
```

The Python harness uses disposable Git repositories, stub external commands, and no credentials. It never runs real workers, pushes, PR creation or tracker edits. Guard and driver tests are not a substitute for the actual user-terminal policy probe or pilot.
