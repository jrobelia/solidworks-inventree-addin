# Roadmap

Last updated: 2026-04-15 (M2 complete; on `milestone-2` branch, PR pending)

Next action: Open PR for `milestone-2` → merge → start M3 planning.

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

- **Search InvenTree by name** -- Look up parts by name (not just IPN) for
  reference -- e.g. checking naming conventions before creating a new part.
- **Assembly flag on Create Part** -- Add an "Assembly" checkbox to the Create
  Part dialog so the part is immediately usable as a BOM parent.

### Future Vision

#### Parking Lot

- If a part is maked as made from automatically mark it as an assembly in 
  SW we could add made form PN in SW properties and qty and add-in could
  auto-populate BOM for us
- Add a link to the invnetree part so you can press the image or something
  similar and it will open the part in a web page.
- How much overhead is it to load the icons from the catagories menu into solidworks?
- Drawing support -- drawings don't get InvenTree part numbers today, probably
  not applicable.
- Status bar showing connection health (green/red dot).
- Per-document-type enable/disable switches in Settings.
- Auto part-number wait toggle -- configurable setting to enable/disable the
  10-second IPN generation poll after Create Part. Useful for servers without
  the auto-numbering plugin installed.

#### Company Specific

- Verify IPN and revision against filename -- detect mismatches when the file is
  named IPN_rev. Company-specific; would need configurable filename pattern.
- Part number naming convention (Coml/Fab/Assy) -- company-specific, needs
  thought before open-sourcing.

#### Considering

- Revision history / PDM-like behavior -- real pain point, unclear if it belongs
  in this add-in or a separate tool.
- BOM snapshot on revision push -- snapshot the InvenTree BOM before applying
  an update. Write a dated JSON file to a configurable archive path.
- Nightly TLA snapshot script -- standalone script that fetches the full
  InvenTree BOM for each TLA on a schedule. Catches InvenTree-only edits.
- BOM line validation -- validate individual InvenTree BOM lines that match
  their SolidWorks counterpart using InvenTree's per-line validated flag?
- Bulk / recursive BOM comparison: compare the full assembly tree (not just
  immediate children) against InvenTree in one operation.

### Not Pursuing

- PDM Standard integration -- PDM Standard (bundled with SW Professional) has
  no public API, so automation is not possible. InvenTree covers part numbering,
  revision recording, and BOM -- sufficient for current needs.

---

## Iterative Milestones

### Milestone 1 -- Part Creation (status: complete)

The add-in can create a new part in InvenTree without leaving SolidWorks,
including category selection and IPN write-back. The task pane also shows
useful read-only InvenTree data for existing parts.

Property mapping configuration ships as a Milestone 1 prerequisite so that
no hardcoded property name strings ever accumulate. Every feature built in
this milestone reads SW custom property names from a user-configurable JSON
file rather than constants in code.

### Milestone 2 -- Assembly BOM Sync (status: complete)

The add-in reads the SolidWorks assembly BOM, compares it against the
InvenTree BOM, and lets the user push selected lines through an interactive
review screen. InvenTree-only lines are never touched. The task pane shows
a live "BOM: N difference(s)" status indicator and the BOM table name.
Duplicate IPN resolution uses revision matching to pick the correct part
when InvenTree returns multiple candidates. Revision ordering and PK-match
gates prevent the compare from running on stale or uncreated assemblies.

### Milestone 3 -- Open-Source Ready (status: future)

Property mapping is already configurable (shipped in Milestone 1). This
milestone focuses on removing remaining company-specific conventions (part
number naming, filename patterns) and verifying the add-in works out of the
box for any SolidWorks + InvenTree shop. Lighter lift than originally scoped.

Two architectural clean-ups also deferred to M3:

- **`TaskPaneViewModel` split** -- At ~1000 lines it has 7 distinct
  responsibilities. Refactor into focused classes (`PartFetchViewModel`,
  `PartPushViewModel`, etc.) with thin orchestration in `TaskPaneViewModel`.
  Requires XAML and code-behind changes.
- **n+1 HTTP queries** -- `GetBomAsync` and `GetPartsByIpnAsync` each fetch
  sub-part details one request at a time. Investigate whether InvenTree offers
  a batch/filter endpoint before implementing a fix.

---

## Actionable Backlog

| # | Task | Milestone | Type | Status | Pass / fail condition |
|---|------|-----------|------|--------|-----------------------|
| 5 | Add a name-based search box to the task pane (searches InvenTree, displays results) | 3 | build | open | User can type a partial name, see matching parts, and view their details |
| 10 | Remove remaining company-specific conventions (part number naming, filename patterns) | 3 | cleanup | open | No company-specific strings remain; add-in works out of the box for any SW + InvenTree shop |
| 14 | Remap task pane UI layout in the Pencil design file (`docs/sw-addin-layout.pen`) to reflect current and planned screens | 3 | design | open | Pencil file has up-to-date frames for all task pane views (part, assembly, create part dialog, info panel, name search) |
| 15 | Allow setting the Assembly flag when creating a part via the add-in | 3 | build | open | Create Part dialog has an "Assembly" checkbox; when checked, `assembly: true` is sent to InvenTree; part is immediately usable as a BOM parent |

### Done

- Tasks 7–9: Assembly BOM sync -- `SwAssemblyBomService` reads SW BOM (immediate children,
  IPN + quantity); `BomCompareViewModel` fetches InvenTree BOM and diffs; `BomCompareWindow`
  shows added / updated / matched / InvenTree-only lines with per-line push selection.
  InvenTree-only lines untouched. BOM status indicator ("BOM: N difference(s)") + BOM table
  name shown in task pane. Duplicate IPN resolution via revision match. Revision ordering
  and PK-match gates guard the compare. Verified 2026-04-15.
- Tasks 4, 12, 13: Read-only InvenTree info panel; property name validation; task pane
  refresh on SW custom property changes. Verified 2026-04-15.
- Tasks 6 + 11: Description row + InvenTree PK storage -- Description synced field with
  match indicator, Push to InvenTree and Apply to SW Doc buttons; PK written to SW doc
  after every fetch and create; PkMatch indicator in task pane; Settings local/shared
  section order corrected. Verified 2026-03-29.
- Tasks 1–3: Create Part in InvenTree from SolidWorks -- category tree dialog, POST new part,
  IPN + Name write-back to SW doc, auto-populate comparison grid. Optional IPN field for
  users without a server-side IPN plugin. Verified 2026-03-28.
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
- Visual polish -- focus rings on all inputs, Segoe MDL2 Assets icons on all
  buttons, status bar icon, lock tooltip on InvenTree read-only fields
- Property mapping configuration (tasks 0a–0c) -- configurable field name
  mapping between SolidWorks and InvenTree; shared or local JSON file;
  `PropertyMappingEditorWindow` dialog; `GetServerInfoAsync()` for version check
- Configurable property mapping -- Moved to Milestone 1 as a prerequisite.
  Ships before part creation so hardcoded property names never accumulate.

---

## Next Action

M1 and M2 complete. Open PR for `milestone-2` → merge to `main` → start M3 planning.
First M3 candidates: task 10 (company-specific cleanup), task 5 (name search), task 15 (Assembly flag).


