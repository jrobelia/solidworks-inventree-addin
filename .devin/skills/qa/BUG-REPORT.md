# Bug Report Template

Use this template when filing a failure issue. Create the issue with:

```powershell
gh issue create --title "bug: <symptom in plain language>" --body-file <temp> --label bug --label needs-triage
```

Body template:

```markdown
## What happened
<actual behavior — from the user's perspective, in plain language>

## What I expected
<expected behavior>

## Steps to reproduce
1. <step — use domain terms from CONTEXT.md; leave out module names>
2. <include relevant inputs or configuration>

## Additional context
<domain observations from codebase exploration — without source-file citations>

## QA metadata
- Severity: P0 / P1 / P2 / P3
- Failure type: PR-blocking / follow-up
- Branch: <current branch>
- PR: #N
- QA step: <step title>
- Evidence: <output, error, or screenshot captured>
```

Omit `Failure type` and `PR` when no PR context is present.
