# Repair a settled work order in fresh context

Read JOB_FILE and contracts.md, all current findings and prior reports under ticket_dir, the supplied specification and approved seam, and the driver's test logs. This is a fresh session; files are the handoff, not the prior agent's unrecorded reasoning.

Fix only the supplied open findings. Use the existing tdd skill where a failing test can pin a correction. Work inside the approved seam. A correction that forces a new consequential decision, public interface, or unrelated change produces blocked with the exact choice.

Preserve carried rulings and standing notes. Update the report with each finding's disposition, targeted red/green evidence, and corrected verification recipes. Stage owned paths and commit a new repair, one git command per tool call. Do not amend, rebase, reset, discard partial work, push, modify runner permissions, edit tracker records, or start another agent.

The driver verifies the repair commit and re-runs both full review axes. You do not certify that your own repair resolves the reviews. Keep any missing criterion evidence visible.

A salvage dispatch reads the existing repair commit and artifacts and writes only a supported verdict. It does not repair again. Insufficient evidence is blocked.

Write report.md and then result.json. Clean means the settled corrections are committed with a clean worktree. A named blocker preserves the worktree and goes to a human handoff.
