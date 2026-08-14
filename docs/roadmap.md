# Roadmap

Last updated: 2026-08-14 (M3 in progress: bug fixes; M4 queued)

Next action: Work through open [Milestone 3](https://github.com/jrobelia/solidworks-inventree-addin/milestone/1) bug fixes on `milestone-3`.

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

- **Assembly flag on Create Part** -- Done. All part flags are exposed in the
  Create Part dialog and the Task Pane (issues `#42`, `#43`, `#34`).

### Future Vision

#### Parking Lot

- Drawing support -- drawings don't get InvenTree part numbers today, probably
  not applicable. #longterm
- Per-document-type enable/disable switches in Settings. #longterm
- If a part is marked as made from automatically mark it as an assembly in
  SW we could add made form PN in SW properties and qty and add-in could
  auto-populate BOM for us #longterm
- Add a name-based search box to the task pane (searches InvenTree, displays results). User can type a partial name, see matching parts, and view their details. #longterm (deferred -- duplicates InvenTree web UI)
- Remap task pane UI layout in the Pencil design file (`docs/sw-addin-layout.pen`) to reflect current screens. #longterm
- Connection health indicator in status bar -- green/red dot showing live server reachability; reuse `GetServerInfoAsync`. #longterm
- Category icons in category picker -- spike overhead of fetching/rendering InvenTree category icons in the SW category tree dialog. #longterm

#### Company Specific

- Verify IPN and revision against filename -- detect mismatches when the file is
  named IPN_rev. Company-specific; would need configurable filename pattern.
- Part number naming convention (Coml/Fab/Assy) -- company-specific, needs
  thought before open-sourcing.
- If a part is marked as made from automatically mark it as an assembly in 
  SW we could add made form PN in SW properties and qty and add-in could
  auto-populate BOM for us #longterm

#### More thought Needed

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

### Milestone 3 -- Open-Source Ready (status: in progress)

Property mapping is already configurable (shipped in Milestone 1). This
milestone focuses on removing remaining company-specific conventions (part
number naming, filename patterns), fixing open bugs, and verifying the add-in
works out of the box for any SolidWorks + InvenTree shop. The architecture
cleanup originally planned here has moved to Milestone 4 (see `#89`).

Architectural clean-ups completed in M3:

- **`TaskPaneViewModel` refactor** *(done, issues #5/#6/#9)* -- `PartSyncSession`
  extracted as a standalone module owning all Apply/Push domain logic and
  thumbnail state. The VM is now an orchestrator: session lifetime, status
  messaging, and UI events only. All preview and enabled/visible backing fields
  deleted; properties are now computed from the session. Full sub-VM split
  (`PartFetchViewModel`, `PartPushViewModel`, etc.) was not implemented and is
  no longer planned.
- **n+1 HTTP queries** *(done, 2026-05-23)* -- `GetBomAsync` now fans out all
  `FetchDetailAsync` calls via `Task.WhenAll` (one request per unique PK in
  parallel). `BomCompareViewModel.LoadAsync` now starts `GetBomAsync` and
  `BuildIpnLookupAsync` concurrently. `GetPartsByIpnAsync` still fetches detail
  sequentially per IPN but `BuildIpnLookupAsync` already parallelizes across
  distinct IPNs via `Task.WhenAll`, making this a second-order problem.
  Batch fetch via `?pk__in=` filter not investigated; deferred to parking lot.

### Milestone 4 -- Architecture & Performance (status: future)

Milestone 4 extracts `TaskPaneState` from `TaskPaneViewModel` to create a small,
UI-free domain seam, then builds performance and reliability improvements on top
of that cleaner state boundary. This reverses the earlier M3 call not to pursue
`IBomReadinessSource` coupling, because the codebase has since outgrown the
single `TaskPaneViewModel` seam.

Tracked in [Milestone 4](https://github.com/jrobelia/solidworks-inventree-addin/milestone/2):

- `#89` — Extract `TaskPaneState` from `TaskPaneViewModel` (parent spec)
- `#90` — Phase A: Introduce `TaskPaneState`
- `#91` — Phase B1: Move document state into `TaskPaneState`
- `#92` — Phase B2: Move session lifecycle and Apply/Push delegation into `TaskPaneState`
- `#93` — Phase C: Contract `TaskPaneViewModel` to a thin projection adapter
- `#65` — Reduce InvenTree round-trips during Compare BOM

---

## Active Work

Work is now tracked in GitHub Milestones rather than this table.

- [Milestone 3 — Bug fixes and stabilization](https://github.com/jrobelia/solidworks-inventree-addin/milestone/1)
- [Milestone 4 — TaskPaneState architecture and performance](https://github.com/jrobelia/solidworks-inventree-addin/milestone/2)

### Done

- Task 10: Company-specific cleanup -- `OA-` prefixed IPN strings replaced with generic `PART-`
  placeholders in all test fixtures; Pencil design mockup updated. Coml/Fab/Assy naming
  conventions and IPN_rev filename patterns were never implemented in production code.
  Verified 2026-05-23.
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

M1 and M2 complete. M3 in progress on the `milestone-3` branch.
M4 (TaskPaneState extraction and performance) is queued behind M3.

Active M3 work: `#84`, `#85`, `#60`, `#61`, `#37`.


