# Roadmap

Last updated: 2026-03-03 (Iteration 0)

## Project North Star

The add-in is a **friction-reducing bridge** between SolidWorks and InvenTree.
An engineer opens a part or assembly in SolidWorks and never needs to leave it
to interact with InvenTree.

For **new parts**, they pick a category, type a name, and the add-in creates
the InvenTree part and writes the generated IPN back into SolidWorks -- the part
is born in both systems simultaneously. For **existing parts**, they see a live
comparison of SolidWorks and InvenTree data -- properties, revision, image, plus
read-only info like stock, supplier, and pricing -- and can sync any field in
either direction with one click. For **assemblies**, they see a side-by-side BOM
comparison showing what matches, what's different, and what only exists in
InvenTree, then choose exactly which lines to push.

The add-in handles edge cases: InvenTree-only BOM lines are never touched, IPN
generation happens server-side with a re-fetch, and property names are
user-configurable so the tool isn't locked to any one company's conventions.

**It does not** replace InvenTree's web interface for purchasing, build orders,
supplier management, or anything beyond the engineer's immediate workflow of
keeping the inventory system in sync while designing.

---

## The Feature Lab

### Immediate Gaps

- **Create new InvenTree part from SolidWorks** -- Browse/search the category
  tree, type a name, create the part, wait for IPN generation, write IPN + name
  back into SW custom properties. Eliminates the #1 daily pain point.
- **Read-only InvenTree info panel** -- Display stock on hand, on order, price,
  active status, and default supplier for the fetched part. Data is already in
  the API response -- just needs to be shown.
- **Search InvenTree by name** -- Look up parts by name (not just IPN) for
  reference -- e.g. checking naming conventions before creating a new part.
- **Description field sync** -- Add InvenTree's description as a synced field
  alongside name, notes, and revision.

### Strategic Expansions

- **BOM export with interactive review** -- Read the SolidWorks assembly BOM
  (immediate children), compare against the InvenTree BOM, show a side-by-side
  diff (added / updated / InvenTree-only), let the user select which lines to
  push. Never delete InvenTree-only lines.
- **Configurable property mapping** -- Let users define which SW custom property
  names map to which InvenTree fields via the Settings UI. Required for
  open-source viability.

### Future Vision / Parking Lot

- Revision history / PDM-like behavior -- real pain point, unclear if it belongs
  in this add-in or a separate tool.
- Drawing support -- drawings don't get InvenTree part numbers today, probably
  not applicable.
- Notes field -- works but rarely used. Keep but deprioritize.
- Part number naming convention (Coml/Fab/Assy) -- company-specific, needs
  thought before open-sourcing.
- Bulk / recursive BOM comparison: compare the full assembly tree (not just
  immediate children) against InvenTree in one operation.
- Verify IPN and revision against filename -- detect mismatches when the file is
  named IPN_rev. Company-specific; would need configurable filename pattern.
- Status bar showing connection health (green/red dot).
- Per-document-type enable/disable switches in Settings.
- BOM snapshot on revision push -- snapshot the InvenTree BOM before applying
  an update. Write a dated JSON file to a configurable archive path.
- Nightly TLA snapshot script -- standalone script that fetches the full
  InvenTree BOM for each TLA on a schedule. Catches InvenTree-only edits.
- BOM line validation -- validate individual InvenTree BOM lines that match
  their SolidWorks counterpart using InvenTree's per-line validated flag.

### Not Pursuing

- PDM Standard integration -- PDM Standard (bundled with SW Professional) has
  no public API, so automation is not possible. InvenTree covers part numbering,
  revision recording, and BOM -- sufficient for current needs.

---

## Iterative Milestones

### Milestone 1 -- Part Creation (status: next)

The add-in can create a new part in InvenTree without leaving SolidWorks,
including category selection and IPN write-back. The task pane also shows
useful read-only InvenTree data for existing parts.

### Milestone 2 -- Assembly BOM Sync (status: future)

The add-in can compare a SolidWorks assembly BOM against InvenTree and push
selected lines through an interactive review screen. Replaces the manual
CSV export workflow.

### Milestone 3 -- Open-Source Ready (status: future)

Property mapping is configurable, hardcoded company conventions are removed,
and the add-in is usable by any SolidWorks + InvenTree shop out of the box.

---

## Actionable Backlog

| # | Task | Milestone | Type | Status | Pass / fail condition |
|---|------|-----------|------|--------|-----------------------|
| 1 | Fetch and display the InvenTree category tree in a dialog | 1 | build | open | User sees a browsable list of categories from their InvenTree server |
| 2 | Create a new InvenTree part (category + name) from the dialog | 1 | build | open | POST to /api/part/ succeeds; new part appears in InvenTree |
| 3 | After creation, re-fetch the part to get the plugin-generated IPN and write IPN + name into SW custom properties | 1 | build | open | PartNo and Description properties are populated in the open SW document without manual typing |
| 4 | Display read-only InvenTree fields (stock, on order, price, active, default supplier) in the task pane after fetch | 1 | build | open | Fields appear below the existing property comparison when a part is loaded |
| 5 | Add a name-based search box to the task pane (searches InvenTree, displays results) | 1 | build | open | User can type a partial name, see matching parts, and view their details |
| 6 | Add Description as a synced property row (same pattern as Name/Notes/Revision) | 1 | cleanup | open | Description row appears in the comparison grid with match indicator and push/apply buttons |
| 7 | Read the SolidWorks assembly BOM (immediate children with IPN + quantity) | 2 | build | open | When an assembly is open, the add-in can list child components and their quantities |
| 8 | Fetch the InvenTree BOM for the same part and diff against the SW BOM | 2 | build | open | Side-by-side comparison shows added, updated, matched, and InvenTree-only lines |
| 9 | Interactive review screen: user selects which BOM lines to push, confirms, add-in writes to InvenTree | 2 | build | open | Only user-selected lines are created/updated; InvenTree-only lines are untouched |
| 10 | Settings UI for custom property name mapping (SW property <-> InvenTree field) | 3 | build | open | User can change which SW property maps to IPN, name, revision, etc. and the add-in uses those mappings |
| 11 | Replace all hardcoded property names with mapped values from settings | 3 | build | open | No property name strings remain in ViewModel or PropertyService code; all read from config |

### Done

- v1.0.0 -- Encrypted settings panel, push revision, fetch part data
- Push part image to InvenTree -- viewport capture via SaveBMP API, crop dialog
  with square-lock and move-drag, 800x800 PNG resize pipeline, "Also push
  image" checkbox on Push Revision confirmation dialog
- Drawing protection -- drawings blocked with amber warning; document type
  stored as first-class ViewModel state
- Bidirectional Name/Notes push -- "Push to InvenTree" / "Apply to SW Doc"
  buttons; RevisionMatch indicator; confirmation dialog with image checkbox
- InvenTree thumbnail display + capture-push button -- 120x120 thumbnail, grey
  placeholder, detail endpoint fetch for full fields

---

## Architectural Impact (Milestone 1)

When Milestone 1 is built, `docs/architecture.md` will need:

- A new **InvenTree category service** (or methods on the existing client) for
  `GET /api/part/category/` and `POST /api/part/`.
- A new **Create Part dialog** (WPF window) with category browser and name
  entry.
- The `InventreePart` data class will need additional fields (`in_stock`,
  `ordering`, `default_supplier`, etc.) or a companion read-only display model.
- The `IInventreeClient` interface will grow: `GetCategoriesAsync()`,
  `CreatePartAsync()`, `SearchPartsByNameAsync()`.
- A new "InvenTree info" section in the task pane XAML below the properties
  grid.

These changes will be reflected in `docs/architecture.md` by the build pipeline
debrief as each feature is completed.

---

## Next Action

Run the build pipeline on **task #1** (fetch and display the category tree)
next. It is the foundation that tasks #2 and #3 depend on.

## Call to Action

**Most critical gap:** The add-in cannot create new parts. Every new part
requires leaving SolidWorks, switching to a browser, creating the part
manually, and copying data back. This is the highest-frequency,
highest-friction task the add-in was built to eliminate.

**Risk of leaving it unaddressed:** The add-in remains useful only for existing
parts -- which means the most common starting point of an engineer's workflow
(creating something new) is completely unsupported. The tool stays a nice-to-have
instead of becoming essential.
