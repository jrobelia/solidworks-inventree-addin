# Adjudicate two independent reviews

Read JOB_FILE and contracts.md. Open both current axis reports and the driver's deterministic logs. Read the ticket and context snapshot, approved seam, current diff, prior adjudications, and carried findings under ticket_dir. You may write this job's report and result only; do not fix code or start another agent.

Verify every finding against its anchor and governing source. Keep Spec and Standards findings separate. Reject a factually wrong or speculative finding with a recorded reason. A cosmetic or non-load-bearing observation can be recorded with a standing reason and revisit condition; do not silently drop it. A consequential trade-off without prior approval produces blocked. Do not claim the maintainer accepted a deferral that has no recorded approval.

Produce the smallest settled work order for genuine defects. Retain finding ids and dispositions in report.md so repairs and later rounds can follow the history. The result findings array contains only open corrections, each with id, anchor, correction, severity, and source. A correction that requires an unapproved public seam or behavior is blocked.

A red full suite or format gate cannot earn clean. Distinguish a code defect introduced by the ticket from an environment or baseline failure. A settled code defect can join the repair work order; an environment failure is blocked with its evidence. Both current axis reports must exist and name the same reviewed commit.

Write report.md including each finding's disposition and all carried notes. Write result.json last: clean when the deterministic gate is green and no correction remains; findings when a settled work order remains; blocked when the next action needs the maintainer. This is permission to proceed to evidence verification, not to merge or certify SolidWorks QA.
