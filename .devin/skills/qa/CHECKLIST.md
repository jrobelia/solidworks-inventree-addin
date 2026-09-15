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

## Smoke test

Run these at the start of the walk, before the issue-specific groups. Treat them as suggestions the engineer can skip, but the agent should recommend the full set and explain why.

The smoke test should cover the major pieces of add-in functionality the diff touches, not the specific issue acceptance criteria. Trace the changed files and methods back to the user-facing flows they participate in and add one broad smoke test per major flow. If multiple major flows are at risk, run the broadest, most user-facing one first.

Use these mappings as a starting point:

- Diff touches the add-in load path, COM registration, or `TaskPane` / XAML / ViewModel startup: smoke test the **add-in loads and the Task Pane renders**.
- Diff touches `TaskPaneViewModel` document identity, `DocumentType`, or `BomSectionVisible`: smoke test **the Task Pane shows document state**.
- Diff touches `FetchPartAsync`, the InvenTree client fetch methods, or `PartSyncSession` creation: smoke test **Fetch works on a plain document**.
- Diff touches `BomCompareViewModel`, `IAssemblyBomService`, or the BOM table service: smoke test **BOM Compare for an Assembly with and without a Part Sync session**.
- Diff touches `CreatePartWindow`, `CreatePartViewModel`, or part creation: smoke test the **Create Part** flow.
- Diff touches `SettingsWindow`, credentials, or `PropertyMapping`: smoke test opening **Settings** and applying the default **Property Mapping**.

If the diff is narrow and the mappings above do not add meaningful coverage beyond the issue groups, fall back to the base list:

1. The add-in loads — the InvenTree Task Pane appears and renders without error when SolidWorks opens a document.
2. The Task Pane shows document state — stamped Document Properties appear and commands sit in their expected enabled states.
3. Fetch works on a plain document — a document with an IPN and no InvenTree Part PK fetches and populates cleanly.
