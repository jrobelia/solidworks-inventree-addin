# QA Checklist Reference

## Severity Guide

| Level | Meaning | Examples |
|-------|---------|---------|
| P0 | Blocker — feature unusable, data loss, security issue | App crashes, data corrupted, auth bypass |
| P1 | High — core behavior broken, no workaround | Primary action fails, wrong data shown |
| P2 | Medium — degraded behavior, workaround exists | Wrong label, slow response, minor wrong output |
| P3 | Low — cosmetic, edge case, minor annoyance | Typo, alignment off, message wording |

## Step Quality Standards

Each step in the test plan must have:

- [ ] A specific, unambiguous action (not "click around" — "click the Save button on the Edit Profile form")
- [ ] A concrete expected result (not "it works" — "the profile page reloads and shows the updated name")
- [ ] Preconditions listed (even if "none")
- [ ] At least one edge case per feature area (empty input, boundary value, error path)

## Issue Quality Rules

- Write from the **user's perspective** — describe behavior, not code
- **No file paths, line numbers, or module names** — they go stale after refactors
- Use **domain language** from `CONTEXT.md` / `UBIQUITOUS_LANGUAGE.md`
- Issues must be **durable** — still make sense after a major refactor
- Prefer **many thin issues** over few thick ones — each should be independently fixable

## Anti-Patterns to Avoid

| Avoid | Why | Instead |
|-------|-----|---------|
| Vague steps ("navigate to settings") | Can't reproduce consistently | Specify exact UI path and action |
| Missing preconditions | Step fails for wrong reason | Document login state, data setup, env requirements |
| No test data | Tester gets blocked | Specify sample inputs or how to generate them |
| Generic bug titles ("button broken") | Hard to triage and search | Be specific: "[Profile] Save button does nothing when display name is blank" |
| Skipping error paths | Miss critical bugs | Always include: what happens with empty input, invalid data, network failure |

## Edge Case Triggers (add at least one per feature area)

- Empty / null / blank inputs
- Maximum length inputs
- Special characters in text fields
- Unauthenticated access to protected routes
- Repeated actions (double-click, double-submit)
- Offline or slow network behavior (if applicable)
