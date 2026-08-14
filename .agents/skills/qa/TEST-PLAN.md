# Test Plan Formats

Use these templates when presenting proposals and the full plan. Print the proposal or plan in the chat response; keep the `ask_user_question` prompt concise. Do not put the full plan inside the `ask_user_question` body — a long question box can break the UI.

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

## GUI functionality testing

Add a GUI functionality group when the change touches the **Task Pane**, a **dialog**, a **control**, or a **data-bound property**. The group must include at least one step for each category:

- **Visibility** — the new or changed control appears and is enabled or disabled as expected.
- **Layout / sizing** — the control or dialog renders without clipping, overlap, or unusable scroll areas.
- **Interaction** — clicking, focusing, typing, toggling, or selecting produces the expected action.
- **Data binding** — the control reflects the current value and updates when the underlying state changes.
- **Error / empty state** — invalid, empty, or edge-case input shows the expected message, disabled state, or validation.

### Example group

**Group 2: #34 — Add InvenTree Flags to Create Part dialog** (5 steps)

1. Verify the **Category tree** remains usable
   - Preconditions: Open a `.sldprt` with no **IPN**; open the **Create Part** dialog
   - Action: Scroll the **Category tree** and select any category
   - Expected: The tree is scrollable, the selected category is highlighted, and no items are clipped by the **InvenTree Flags** section

2. Verify the **InvenTree Flags** section is visible
   - Preconditions: The **Create Part** dialog is open
   - Action: Inspect the area below the **Category tree**
   - Expected: The **InvenTree Flags** list is fully visible, not overlapped, and its items are readable

3. Toggle an **InvenTree Flag** and confirm the selection updates
   - Preconditions: The **Create Part** dialog is open with a category selected
   - Action: Check one flag, then check a second flag
   - Expected: Each checked flag appears selected and the summary reflects the selected flags

4. Verify **data binding** from the document to the dialog
   - Preconditions: A `.sldprt` with an existing **IPN** is open
   - Action: Open the **Create Part** dialog
   - Expected: The dialog shows the existing **IPN** and the **Category tree** highlights the matching category

5. Verify the empty-state behavior
   - Preconditions: The **Create Part** dialog is open with no category selected and no flags checked
   - Action: Attempt to create the part
   - Expected: The **Create Part** button is disabled or a validation message explains that a category is required

## Step quality reminder

Every step must be:

- A specific action in the SolidWorks InvenTree Add-In GUI
- A concrete observable result the user can see (e.g. "the preview shows the part name")
- Preconditions listed, even if "none"
- Phrased in domain terms from `CONTEXT.md` (Task Pane, IPN, InvenTree Part PK, Fetch, Apply, Push, Part Sync, BOM Compare, etc.)
- Stays in the GUI and domain language; source files, diffs, and line numbers are not test-step content
- Covering at least one edge case per feature area
