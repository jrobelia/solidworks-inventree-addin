# Bug Report Template

Used by section 3 when filing a failure issue. Call `create_issue` with:

- `title`: `"bug: <symptom in plain language>"`
- `labels`: `["bug", "needs-triage"]` (or labels from `docs/agents/triage-labels.md`)
- `body`:

```markdown
## What happened
<actual behavior — from user's perspective, in plain language>

## What I expected
<expected behavior>

## Steps to reproduce
1. <step — use domain terms, not module names>
2. <include relevant inputs or configuration>

## Additional context
<domain observations from codebase exploration — no file citations>

## QA metadata
- Severity: P0 / P1 / P2 / P3  ← see CHECKLIST.md
- Failure type: PR-blocking / follow-up  ← omit when not in PR context
- Branch: <current branch>
- PR: #N  ← omit when not in PR context
- QA step: <step title>
- Evidence: <output, error, or screenshot captured>
```
