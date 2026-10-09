# Independent read-only review

Read JOB_FILE and contracts.md. The phase chooses exactly one axis: review-spec or review-standards. Read the complete supplied ticket and context snapshot, approved seam, implementer reports under ticket_dir, deterministic check logs, and previous findings. Pin base and input_head; review the entire committed base...input_head range on every round.

For review-spec, use the checklist under "Your task" in .devin/agents/review-spec.md. Check every criterion, shared invariant, conditional rule, seam obligation, and implementation claim. Quote the governing spec and anchor every finding in the diff.

For review-standards, read docs/agents/coding-standards.md. Use the "Fowler smell baseline" and "Your task" checklists in .devin/agents/review-standards.md. Project standards override generic smells. When UI changed, read the design language and inspect relevant captured PNGs.

Those profile files are checklist references only. Their subagent tool restrictions, output transport, dispatch, and Ready-to-merge wording do not apply. Use the full CLI tools available to this session. The driver's pinned format and full-suite logs supply those deterministic checks; do not duplicate them without a named reason.

Do not invoke review, code-review, build-hitl, or build-afk. Do not edit or commit source, run remote actions, or start another agent. You may write only this job's reports and result. A passing review is not host verification.

Confirm carried rulings and standing notes still have valid anchors and reasons. Do not re-flag a settled item unless its revisit condition holds. Record all genuine findings, including cosmetic ones, with an id, severity, anchor, governing rule or spec quote, and the smallest settled correction. An unsettled architectural or domain choice is blocked with the exact question, rather than an improvised repair.

Write report.md with complete coverage, evidence, findings, and concerns for this axis alone. Write result.json last. Clean means no unresolved findings for this axis; findings supplies a non-empty work order for adjudication. Preserve independence from the implementation session.
