# QA Checklist Reference

## Severity Guide

| Level | Meaning | Examples |
|-------|---------|---------|
| P0 | Blocker — feature unusable, data loss, crash | SolidWorks crashes, data corrupted, add-in fails to load, Task Pane is blank |
| P1 | High — core behavior broken, no workaround | Primary action fails (Fetch, Apply, Push), wrong InvenTree data shown |
| P2 | Medium — degraded behavior, workaround exists | Slow response, wrong label, minor wrong output, field not applied |
| P3 | Low — cosmetic, edge case, minor annoyance | Typo, alignment off, message wording, non-blocking UI glitch |

## Step Quality Rules

Every test step must have:

- A specific, unambiguous GUI action (e.g. "Click the Fetch button in the Task Pane after entering IPN `DEMO-001`")
- A concrete observable result (e.g. "The Task Pane State shows POPULATED and the preview displays the part name")
- Preconditions listed (even if "none")
- At least one edge case per feature area
- Domain language from `CONTEXT.md` (Task Pane, IPN, Fetch, etc.) instead of source paths or code terms

## Edge Case Triggers (add at least one per feature area)

- Empty / blank / null IPN
- IPN not found in InvenTree
- Maximum length values in text fields
- Special characters in IPN or property values
- No network or InvenTree server unavailable
- Unauthenticated or expired API key
- Repeated actions (double-click, double-submit)
- No active SolidWorks document
- Wrong Document Type (e.g. BOM Compare on a Part)
- Missing BOM table or BOM Keyword not found
- Large assembly / BOM with many lines

## Anti-Patterns

| Avoid | Why | Instead |
|-------|-----|---------|
| Vague steps ("navigate to settings") | Cannot reproduce consistently | Specify exact Task Pane control and action |
| Missing preconditions | Step fails for wrong reason | Document open document, IPN, server reachability |
| No test data | Tester gets blocked | Provide sample IPN or property values |
| Generic bug titles ("button broken") | Hard to triage and search | Be specific: "[Task Pane] Fetch does nothing when IPN is blank" |
| Skipping error paths | Miss critical bugs | Include empty IPN, not-found, and offline behavior |
| Source-file references in issues | Go stale after refactors | Describe the symptom in domain terms from `CONTEXT.md` |
