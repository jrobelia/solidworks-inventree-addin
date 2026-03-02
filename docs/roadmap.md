# Roadmap

Features and improvements planned for future work.
One line each -- details get fleshed out in the Orchestrator pipeline when work begins.

## Next up
0. We shoudl pull the invntree image with the get data from inventree, change button to load data instead of properties.  How do we lay it out?  Since we will only ever have one image the left right symetry won't hold. We could put the image on the right "inventree" side.  Maybe share space with the part number and load data button on the left solid works side.
1. This shoudl proably be a seperate window. Custom property mapping in Settings: allow users to link each SW custom property name to its
   InvenTree counterpart (e.g. map "OA_IPN" → IPN, "Description" → name). Dropdown list of
   available InvenTree fields; already-selected fields excluded from other dropdowns. This is also
   where per-document-type enable/disable switches belong (Part / Assembly / Drawing on/off) --
   the add-in already knows the document type at load time, so toggling behaviour per type is
   straightforward once the Settings infrastructure exists. Could a SW custom property template
   file (.prtprp / .asmprp) drive the available field list automatically? Could there be a way to add extra properties if the default amount are not enough, or should we just have them all?
1. Ask the question, could a custom property file be used to auto populate or guide the creation of the UI.
2. Create new InvenTree part number directly from SolidWorks (no browser needed)
3. InvenTree BOM snapshot on revision push -- when Push Revision fires in the add-in, snapshot
   the full recursive InvenTree BOM for the parent TLA before applying the update. Write a dated
   JSON file to a configurable archive path. Requires a new archive path field in the settings panel.
4. Nightly TLA snapshot script -- standalone Python or PowerShell script, run via Windows Task
   Scheduler, that fetches the full InvenTree BOM for each TLA in a config list and writes a dated
   snapshot file to the archive drive. Catches InvenTree-only edits (FAB BOMs, PCB quantities) that
   the add-in cannot see. Both this and item 3 produce the same file format so any diff tool
   (WinMerge, VS Code) can compare any two snapshots.
5. BOM checker (read) -- when an assembly is open, serve a different task pane view showing the
   immediate children from SolidWorks alongside the InvenTree BOM for the same part. Flag mismatches
   in part number, revision, and quantity. Read-only; no writes. Full recursive tree view is a
   future addition (users work bottom-up, starting at sub-assembly level).
6. BOM writer -- from the BOM checker view, allow the user to push the SW immediate-child BOM to
   InvenTree. Creates missing lines and updates quantities. Never deletes a line without explicit
   per-line user confirmation (protects InvenTree-only parts such as PCBs).
7. BOM line validation -- from the BOM checker view, validate individual InvenTree BOM lines that
   match their SolidWorks counterpart (part number, revision, and quantity all agree). Uses
   InvenTree's per-line validated flag, not the whole-BOM flag. Lines that exist only in InvenTree
   (consumables, PCBs, bought-in parts) are left unvalidated -- giving reviewers a clear visual
   signal of which lines have been cross-checked against SolidWorks and which have not.

## Far Future ideas
- Bulk sync: push/pull all open assembly parts in one operation
- Show part thumbnail from InvenTree in the task pane
- Auto-detect IPN from filename when custom property is missing
- BOM checker recursive view: show full assembly tree, not just immediate children
- Status bar showing connection health (green/red dot)
- Per-document-type enable/disable switches in Settings (Part on, Assembly on, Drawing off by
  default). Drawings are currently blocked outright -- this would make it configurable. Fits
  naturally alongside roadmap item 1 (custom property mapping).

## Pipeline / tooling improvements
- Security checklist baked into Stage 7 (code review) on every pipeline run -- not a separate step.
  Covers: boundary validation, credential handling, token storage, HTTPS enforcement, no hardcoded
  secrets, no shell injection, error message hygiene. See `.github` repo agent instructions.

## Not pursuing
- PDM Standard integration -- PDM Standard (bundled with SW Professional) has no public API,
  so automation is not possible. Requires active subscription to activate anyway.
  InvenTree covers part numbering, revision recording, and BOM -- sufficient for current needs.

## Done
- v1.0.0 -- Encrypted settings panel, push revision, fetch part data
- Push part image to InvenTree -- viewport capture via SaveBMP API, crop dialog with
  square-lock and move-drag, 800x800 PNG resize pipeline, "Also push image" checkbox
  on Push Revision confirmation dialog
- Drawing protection -- drawings blocked with amber warning; document type stored as
  first-class ViewModel state (_currentDocumentType) ready for per-type feature work
- Bidirectional Name/Notes push -- "Apply to InvenTree →" buttons added to Name and Notes rows;
  RevisionMatch indicator (= / ≠) added to Revision row to match Name/Notes rows;
  confirmation dialog parameterised so each field shows its own message with image checkbox
  defaulting on for Revision and off for Name/Notes
