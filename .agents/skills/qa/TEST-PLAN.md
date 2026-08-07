# Test Plan Formats

Use these templates when presenting proposals and the full plan. Print them to the chat panel; keep the `ask_user_question` prompt concise.

## Group proposal

```
Proposed Test Groups (N groups, N issues):

**Group 1: #12 — Add save button**
**Group 2: #15, #16 — Profile edit and display** (interdependent: display depends on save)
**Group 3: #18 — Delete account**

Approve this grouping, or merge/split groups before we continue?
```

## Full test plan

```
Here is the test plan — N Test Groups, N steps total.

**Group 1: #12 — Add save button** (3 steps)
1. [Step title]
   - Preconditions: [setup needed, or "none"]
   - Action: [user action in the SolidWorks InvenTree Add-In GUI]
   - Expected: [observable result]

**Group 2: #15, #16 — Profile edit and display** (5 steps)
1. ...

Ready to start? Approve, edit, or reorder before we continue.
```

## Step quality reminder

Every step must be:

- A specific action in the SolidWorks InvenTree Add-In GUI
- A concrete observable result the user can see (e.g. "the preview shows the part name")
- Preconditions listed, even if "none"
- Phrased in domain terms from `CONTEXT.md` (Task Pane, IPN, InvenTree Part PK, Fetch, Apply, Push, Part Sync, BOM Compare, etc.)
- Stays in the GUI and domain language; source files, diffs, and line numbers are not test-step content
- Covering at least one edge case per feature area
