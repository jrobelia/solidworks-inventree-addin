# Roadmap

Last updated: 2026-04-14 (M1 complete; feature/bom branch next)

Next action: On `milestone-2` branch — begin Task 7 (read SW assembly BOM).

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

### Milestone 2 -- Assembly BOM Sync (status: in parallel)

The add-in can compare a SolidWorks assembly BOM against InvenTree and push
selected lines through an interactive review screen. Replaces the manual
CSV export workflow.

Developed on the `milestone-2` branch cut from `milestone-1`. The task pane
assembly state shows a "Compare BOM" button and a one-line BOM status
indicator (e.g. "2 differences"); the full comparison lives in its own
dialog. Task 14 reserves these two elements in the task pane layout so the
branch has a known wiring target at merge time.

### Milestone 3 -- Open-Source Ready (status: future)

Property mapping is already configurable (shipped in Milestone 1). This
milestone focuses on removing remaining company-specific conventions (part
number naming, filename patterns) and verifying the add-in works out of the
box for any SolidWorks + InvenTree shop. Lighter lift than originally scoped.

---

## Actionable Backlog

| # | Task | Milestone | Type | Status | Pass / fail condition |
|---|------|-----------|------|--------|-----------------------|
| 14 | Remap task pane UI layout in the Pencil design file (`docs/sw-addin-layout.pen`) to reflect current and planned screens | 3 | design | open | Pencil file has up-to-date frames for all task pane views (part, assembly, create part dialog, info panel, name search) |
| 4 | Display read-only InvenTree fields (stock, on order, price, active) in the task pane after fetch | 1 | build | done | Fields appear below the existing property comparison when a part is loaded; default supplier deferred (API changed in recent InvenTree versions) |
| 5 | Add a name-based search box to the task pane (searches InvenTree, displays results) | 3 | build | open | User can type a partial name, see matching parts, and view their details |
| 13 | Refresh the task pane comparison grid when SW custom properties are applied (user clicks Apply in the SW sidebar, or confirms the save-changes prompt) | 1 | build | done | After the user applies SW custom property changes, the task pane re-reads SW properties and updates the comparison grid without requiring a document switch or reopen |
| 12 | Validate that SW custom property names in the mapping config actually exist in the open document before writing | 1 | cleanup | done | If a mapped property name does not exist in the document, a dialog shows the missing name(s) with OK (skip write) and Cancel (abort operation); no new properties are silently created |
| 7 | Read the SolidWorks assembly BOM (immediate children with IPN + quantity) | 2 | build | open | When an assembly is open, the add-in can list child components and their quantities |
| 8 | Fetch the InvenTree BOM for the same part and diff against the SW BOM | 2 | build | open | Side-by-side comparison shows added, updated, matched, and InvenTree-only lines |
| 9 | Interactive review screen: user selects which BOM lines to push, confirms, add-in writes to InvenTree | 2 | build | open | Only user-selected lines are created/updated; InvenTree-only lines are untouched |
| 10 | Remove remaining company-specific conventions (part number naming, filename patterns) | 3 | cleanup | open | No company-specific strings remain; add-in works out of the box for any SW + InvenTree shop |
| 15 | Allow setting the Assembly flag when creating a part via the add-in | 1 | build | open | Create Part dialog has an "Assembly" checkbox; when checked, `assembly: true` is sent to InvenTree; part is immediately usable as a BOM parent |
| 5 | Add a name-based search box to the task pane (searches InvenTree, displays results) | 3 | build | open | User can type a partial name, see matching parts, and view their details |
| 14 | Remap task pane UI layout in the Pencil design file (`docs/sw-addin-layout.pen`) to reflect current and planned screens | 3 | design | open | Pencil file has up-to-date frames for all task pane views (part, assembly, create part dialog, info panel, name search) |

### Done

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

## Architectural Impact (Milestone 1)

When Milestone 1 is built, `docs/architecture.md` will need:

**Property mapping config (tasks 0a–0c):**
- `PropertyMappingConfig` data class and `IPropertyMappingProvider` interface
  in `Config/`.
- `PropertyMappingProvider` concrete implementation handling file I/O, source
  path resolution, and copy-to-local flow.
- `ServerConfig` gains a nullable `MappingSourcePath` string field to persist
  the configured source path alongside existing credentials (stays DPAPI-
  encrypted since it lives in the same file).
- `IInventreeClient` grows: `GetServerInfoAsync()` returning server version
  string and API version int.
- `PropertyMappingEditorWindow` dialog in `UI/`.
- Module boundary note: `PropertyMappingProvider` does not call
  `IInventreeClient` directly -- version strings are passed in by the UI
  ViewModel.

**Part creation (tasks 1–6):**
- A new **InvenTree category service** (or methods on the existing client) for
  `GET /api/part/category/` and `POST /api/part/`.
- A new **Create Part dialog** (WPF window) with category browser and name
  entry.
- The `InventreePart` data class will need additional fields (`in_stock`,
  `ordering`, `default_supplier`, etc.) or a companion read-only display model.
- `IInventreeClient` also grows: `GetCategoriesAsync()`, `CreatePartAsync()`,
  `SearchPartsByNameAsync()`.
- A new "InvenTree info" section in the task pane XAML below the properties
  grid.

These changes will be reflected in `docs/architecture.md` by the build pipeline
debrief as each feature is completed.

**Custom property refresh (task 13):**
- Three document-level events on `PartDoc` and `AssemblyDoc` cover all cases:
  `AddCustomPropertyNotify`, `ChangeCustomPropertyNotify`,
  `DeleteCustomPropertyNotify`.
- Signatures (all return `int`): Add/Delete take `(string propName, string
  Configuration, string Value, int valueType)`; Change adds `string oldValue`
  before the new value.
- The `Configuration` parameter is `""` for document-level (Custom tab)
  properties — ignore events where it is non-empty (config-specific properties
  the add-in does not read).
- These are **document-level** events (on `PartDoc`/`AssemblyDoc` concrete
  classes, not on `SldWorks`). `SwAddin.cs` must track the currently subscribed
  document object and swap subscriptions on every `ActiveDocChangeNotify` /
  `DocumentLoadNotify2` / disconnect.
- On any of the three events (filtered to `Configuration == ""`), call
  `_taskPaneControl?.RefreshCurrentProperties()`. The ViewModel method already
  exists; no ViewModel changes needed.

---

## Next Action

Tasks 1–4, 6, 11–13 complete. Next: **task 5** (name-based search) and **task 14** (Pencil UI remap).


