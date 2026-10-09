# Implement one approved ticket

Read JOB_FILE, then the contracts.md beside this prompt package. Read the worktree's AGENTS.md, GLOSSARY.md, coding standards, and the complete snapshot of the ticket, comments, parent, and closed blockers. Read referenced ADRs and canonical specifications before editing. The supplied seam is approved; missing consequential decisions produce blocked with the exact choice.

Work only in the supplied worktree. Use the existing tdd skill for one failing test followed by the smallest passing implementation at the supplied seam. The user already approved that seam; do not invent another. Capture targeted red and green outputs in job_dir. For documentation or build-only work, use the exception stated by the ticket and record why no regression test applies.

When UI behavior changes, read the design language. Reuse existing WPF tests and the SurfaceCapture catalog; add a necessary surface or state during implementation, not verification. Keep windows off every monitor through HiddenTestWindow. Do not run the legacy standalone-harness instructions that build the SolidWorks-facing DLL.

Self-review against every criterion and approved seam. Stage only owned paths, one git command at a time. Commit without amending history. Never push, assign tickets, create a PR, change the runner configuration, or dispatch another agent. Full tests and format checks are the driver's authoritative gate after your committed result.

Write report.md in job_dir with decisions, touched seams, red/green evidence, concerns, and one verification recipe per criterion: entry point, fixture state, expected signal, test or capture. Preserve existing partial work on a retry. A salvage dispatch reads the existing commit and evidence and reports only what they support; it does not implement again.

Final action: write result.json according to contracts.md. Clean means committed, clean worktree and complete implementation; it does not mean SolidWorks QA passed. Missing evidence or an unsettled public interface is blocked, not a guess.
