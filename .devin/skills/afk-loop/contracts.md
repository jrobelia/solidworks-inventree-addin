# AFK loop contracts

## Module design

The public interface is the Bash/PowerShell entry point: prepare a finite ticket set, smoke-test worker policy, execute an approved run, or resume its checkpoint. Inputs include the confirmed base, ticket snapshots, approved seams and evidence mapping, bounded repair budgets, and separately approved remote effects. Outputs are checkpoint JSON, timestamped progress, pinned deterministic logs, phase reports, evidence, and optional draft PR references.

The worker seam is job.json plus result.json. Production adapters are local CLI, GitHub CLI, Git, and dotnet processes. Regression adapters exercise the same entry point with stub commands and isolated real Git repositories. Deleting the driver restores worktree coordination, retries, evidence validation, and publication state to every agent prompt. Internal Python helpers own those mechanics; the two shell adapters do not duplicate them.

## Source and dependency disposition

The starting reference is the toolkit AFK loop at commit `29445dd39ca7be0d05e85ff2999c2ae6a3354a0c`. This is an adapted workflow, not a copy of its plugin environment.

| Source | Local treatment |
| --- | --- |
| Launcher, dispatch sequence, bounded ladder, watchdog, replay and transcripts | Retained as local driver behavior |
| `afk-implement`, `afk-review`, `afk-repair`, `afk-verify` | Internal prompt files, not discoverable subskills |
| `inventree-ticket-build`, plugin paths and branch rules | This repo's AGENTS.md, tracker and PR conventions |
| `review-inventree-ticket` and subagent dispatch | Independent CLI axes plus read-only adjudication |
| Reviewer criteria | Existing `.devin/agents/review-spec.md` and `review-standards.md` checklist headings; no profile dispatch |
| `test-inventree-plugin`, Docker, editable installs and live server | Isolated dotnet tests and existing WPF captures |
| `pr` | Driver-generated body following local PR conventions, with acceptance evidence and complete review reports |
| `retro`, gist and image/video uploading, older-gh attachment fallback | Deferred; local logs and artifacts remain available |
| Automatic frontier queries, parallel implementation and stacked/batched merges | Deferred; explicit finite serial intake only |

Shared skill dependencies are `tdd`, `codebase-design`, and `domain-voice`, each already present here. Human preparation uses `spec-playback`; completion hands off to `qa`. Workers do not call orchestration skills. The legacy standalone WPF harness contains host-build instructions that do not apply to this loop; use the isolated test output and off-screen capture catalog instead.

## Approval

`--prepare --tickets N ... --base <branch>` performs read-only tracker queries and writes `.scratch/afk-loop/<run>/APPROVAL.json`. It does not assign tickets, create worktrees, run workers, or publish. Native blockers and body-level `Blocked by` references both apply. Parent and closed-blocker bodies and comments are included. The actual canonical checkout must match the pinned base when execution begins.

Before `--run <run>`, confirm playback and every consequential ruling. Populate each ticket's seam declaration (consumed interface, production/test adapters, deletion-test result) and the complete acceptance mapping:

```json
{
  "id": "AC1",
  "text": "The approved acceptance criterion",
  "kind": "automated"
}
```

Kinds are `automated`, `visual`, `host`, and `not-applicable`. Host checks are never certified by this agent. Not-applicable must be a confirmed scope ruling. New choices discovered after launch return blocked.

Set `confirmed` only after human approval. `policy_verified` is earned by the separate user-terminal `--smoke <run>` probe. `allow_claim` and `allow_publish` default false; the corresponding runtime flags are separate authorizations. These flags are not permission to merge, close issues, release claims, delete branches, remove existing worktrees, or upload evidence.

Prior work is reported from open PRs, matching branches and merged ancestry. First-version intake stops for maintainer disposition rather than adopting or discarding it. Read ticket comments for referenced canonical specifications and ADRs; unresolved links or contradictions remain blockers. GitHub assignment records ownership but is not an atomic distributed lock. The OS-released repository lock serializes local loop instances across worktrees.

## Worker job

The driver creates an immutable job.json per dispatch. The first prompt line identifies its absolute path. Read the contract using the job's `contracts` path, not the current working directory.

Job fields include version, ticket, phase, round, attempt, input_head, base, worktree, job_dir, ticket_dir, package_dir, contracts, approved criteria and seam, full specification snapshot, current findings, deterministic checks, axis reports, previous verification, and writable/salvage flags.

Every command runs from worktree. Implementation and repair may edit approved source and write this job's artifacts. Review, adjudication and verification write this job's reports/evidence only. The policy probe is separate from ticket implementation.

A salvage dispatch is read-only bookkeeping on existing work. It must verify the previous reports and targeted evidence support its claims. A missing report or unproven implementation is blocked, not an invented pass. The driver still runs the authoritative full gate afterward.

## Worker result

Final action: persist job_dir/result.json. Chat is not the routing channel. Use UTF-8 JSON; UTF-8 BOM and Windows newlines are accepted. Never source a result as shell code.

```json
{
  "version": 1,
  "ticket": 50,
  "phase": "review-spec",
  "round": 1,
  "attempt": 2,
  "input_head": "<exact dispatched commit>",
  "head": "<exact resulting or exercised commit>",
  "status": "clean",
  "reason": "",
  "report": "report.md",
  "findings": [],
  "criteria": []
}
```

Use the job's exact identity fields. Status is clean, findings, or blocked. Reports must be non-empty and resolve inside job_dir. An implementation/repair clean result requires a committed change and a clean worktree. Read-only phases must preserve the dispatched commit and source. Blocked names the precise missing decision, evidence, permission, or environment requirement.

Findings contain distinct ids, severity, an exact anchor, the governing source, and a settled correction. Open findings go to adjudication or repair; complete dispositions and carried notes remain in reports. A fresh repair inherits the work order and files, not the implementation session's history. Both full review axes run again after a repair; no delta-review optimization is implied.

Verification accounts for every approved criterion exactly once:

```json
{
  "id": "AC1",
  "status": "automated",
  "artifact": "jobs/verify-r1-a5/evidence.txt",
  "reason": ""
}
```

Automated and visual rows name non-empty artifacts relative to ticket_dir; visual requires PNG evidence. A not-proven automated/visual criterion produces findings with the missing proof and settled correction. Host rows use host-pending and a reason containing the GUI prerequisites, action, and expected result. Not-applicable gives its confirmed scope reason. Verification cannot change the approved evidence kind to escape a check.

## Deterministic gate

The driver owns the full gate on the committed head:

- `dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers`
- `dotnet format "Solidworks Inventree Add-In.sln" --verify-no-changes`

It records commands, exit codes, log hashes, and the unchanged clean commit. A green baseline is required before implementation. A red changed-code gate can inform review and a settled repair, but cannot earn clean adjudication or publication. Environment failure is blocked. The normal suite does not include Explicit captures; request the required SurfaceCapture test by filter, inspect its PNG, and copy it with a single scoped cp command into job_dir.

No dotnet build, host output replacement, registration, SolidWorks closure or restart is part of unattended work. Tests write the add-in to bin_unit_test. The screenshot catalog is not universal coverage; implementation adds only the surface/state a ticket needs.

## Checkpoints and recovery

STATUS.json is atomically replaced after transitions; PROGRESS.md appends timestamps. Logs, prompt files, jobs and reports use unique attempts. SessionStart records the exact child session id; the driver never chooses the newest unrelated CLI session. Implementation/repair start fresh. Each axis and verification may resume its own session.

A missing verdict is not a pass, even with exit code zero. Retry a verdictless empty crash; nudge a known live session or salvage existing work without repeating implementation. An expired resume id falls back to fresh context outside the infrastructure retry budget. Timeouts terminate the spawned process tree only. Local locks release when the process exits.

Resume keeps the original base, approval, package fingerprint and budgets. Re-fetch bodies/comments and reject spec drift, a changed base, an unrelated moved worktree, missing or changed logs/reports/artifacts, and corrupt checkpoints. A moved or dirty in-flight implementation remains preserved; never discard it automatically. Recorded evidence is not replayed after the code it certifies changes.

Default review budget is three review rounds, allowing at most two regular repairs with confirming review. Default post-verification budget is one repair-confirm cycle. Infrastructure retries are separate. Budgets are bounded and configurable; an exhausted ladder produces a human handoff.

## Policy and publication

Generated worker configuration lives under `.devin/afk-loop/<run>/` and is ignored by Git. Each worktree gets only owned scoped local permissions, never a copy of broad personal grants. Existing worktree configuration is preserved and blocks automatic replacement. Subagents are disabled. A PreToolUse guard restricts commands, paths, protected metadata, remote effects and nested workflows. SessionStart records attribution.

Windows permissions are not OS isolation. The installed CLI must load and honor this policy in the actual user-terminal smoke test; unit tests of a guard cannot prove that. Missing grants or organization restrictions remain blockers. The launcher does not silently use dangerous mode, change existing policies, copy credentials, or unset authentication variables.

With separately approved --publish, publish only the converged commit, create/update a draft on the confirmed base, include acceptance mapping, deterministic commands, full review notes, host checks and /qa handoff, and check CI. Keep publication/CI failure visible; a pushed branch or draft does not certify success. No log, image, transcript or gist upload is automatic. Claims remain recorded on pause/block for a named next owner; abandonment or release requires human disposition.

Final success is ready-for-qa. Only the existing human /qa workflow can apply qa-verified, promote a draft or request a merge.
