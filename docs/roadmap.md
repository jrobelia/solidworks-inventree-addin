# Roadmap

Features and improvements planned for future work.
One line each -- details get fleshed out in the Orchestrator pipeline when work begins.

## Next up
1. Push part image to InvenTree -- capture the current SolidWorks viewport as an image (parts and
   assemblies both supported). Open a preview dialog where the user can drag a crop rectangle and
   confirm the result. Resize to a maximum of 800x800 pixels before uploading so thumbnails load
   quickly in InvenTree. Add-in uploads to InvenTree's part image endpoint and discards the temp
   file. Acts as a quality gate -- user can cancel, re-orient the model, and retry.
2. Allow manual entry of Custom property names in the settings window.  Allow users to link SW custom property to inventree property, such as IPN and part name, etc.
3. Ask the question, could a custom property file be used to auto populate or guide the creation of the UI.
4. Create new InvenTree part number directly from SolidWorks (no browser needed)
5. InvenTree BOM snapshot on revision push -- when Push Revision fires in the add-in, snapshot
   the full recursive InvenTree BOM for the parent TLA before applying the update. Write a dated
   JSON file to a configurable archive path. Requires a new archive path field in the settings panel.
6. Nightly TLA snapshot script -- standalone Python or PowerShell script, run via Windows Task
   Scheduler, that fetches the full InvenTree BOM for each TLA in a config list and writes a dated
   snapshot file to the archive drive. Catches InvenTree-only edits (FAB BOMs, PCB quantities) that
   the add-in cannot see. Both this and item 5 produce the same file format so any diff tool
   (WinMerge, VS Code) can compare any two snapshots.
7. BOM checker (read) -- when an assembly is open, serve a different task pane view showing the
   immediate children from SolidWorks alongside the InvenTree BOM for the same part. Flag mismatches
   in part number, revision, and quantity. Read-only; no writes. Full recursive tree view is a
   future addition (users work bottom-up, starting at sub-assembly level).
8. BOM writer -- from the BOM checker view, allow the user to push the SW immediate-child BOM to
   InvenTree. Creates missing lines and updates quantities. Never deletes a line without explicit
   per-line user confirmation (protects InvenTree-only parts such as PCBs).
9. BOM line validation -- from the BOM checker view, validate individual InvenTree BOM lines that
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

## Not pursuing
- PDM Standard integration -- PDM Standard (bundled with SW Professional) has no public API,
  so automation is not possible. Requires active subscription to activate anyway.
  InvenTree covers part numbering, revision recording, and BOM -- sufficient for current needs.

## Done
- v1.0.0 -- Encrypted settings panel, push revision, fetch part data
