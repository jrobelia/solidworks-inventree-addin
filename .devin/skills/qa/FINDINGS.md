# Findings Ledger

Each QA run keeps a ledger at `.scratch/qa/<run>/findings.md`. The ledger is the single source of truth for the run: what failed, what was skipped, and how each finding was disposed. The merge gate and the end-of-QA summary read from it.

It is a disposable run artifact, not project documentation — it lives in `.scratch` and dies with the folder.

Record a finding the moment a step fails, before the walk continues, so the disposition pass sees the run's full failure set at once.

## Entry format

Append one block per failure:

```markdown
## F<n> — <short symptom title in domain language>
- Step: Group <X> Step <N> — <step title>
- Severity: P0 / P1 / P2 / P3
- Proposed blocking: yes / no — the agent's read; the user settles it in the disposition pass
- Observed: <what happened, from the user's perspective>
- Expected: <what should have happened>
- Evidence: <error text, observation, screenshot reference>
- Domain notes: <codebase context in domain terms — no file paths or line numbers>
- Disposition: pending → fixed (<commit>) | filed #<N> (blocking | follow-up) | wontfix #<N> | parked
```

Smoke test failures use the same format with `Step: Smoke <N>` and `Proposed blocking: yes`.

Skipped steps are tracked in the plan, not the ledger — unless the skip conceals a suspected problem worth recording.
