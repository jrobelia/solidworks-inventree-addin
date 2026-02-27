# Roadmap

Features and improvements planned for future work.
One line each -- details get fleshed out in the Orchestrator pipeline when work begins.

## Next up
1. Allow manual entry of Custom property names in the settings window.  Allow users to link SW custom property to inventree property, such as IPN and part name, etc.
2. Ask the question, could a custom property file be used to auto populate or guide the creation of the UI.
3. Create new InvenTree part number directly from SolidWorks (no browser needed)
4. InvenTree BOM snapshot on revision push -- when Push Revision fires in the add-in, snapshot
   the full recursive InvenTree BOM for the parent TLA before applying the update. Write a dated
   JSON file to a configurable archive path. Requires a new archive path field in the settings panel.
5. Nightly TLA snapshot script -- standalone Python or PowerShell script, run via Windows Task
   Scheduler, that fetches the full InvenTree BOM for each TLA in a config list and writes a dated
   snapshot file to the archive drive. Catches InvenTree-only edits (FAB BOMs, PCB quantities) that
   the add-in cannot see. Both this and item 4 produce the same file format so any diff tool
   (WinMerge, VS Code) can compare any two snapshots.

## Far Future ideas
- Bulk sync: push/pull all open assembly parts in one operation
- Show part thumbnail from InvenTree in the task pane
- Auto-detect IPN from filename when custom property is missing
- Assembly BOM comparison: SolidWorks BOM vs InvenTree BOM
- Status bar showing connection health (green/red dot)

## Not pursuing
- PDM Standard integration -- PDM Standard (bundled with SW Professional) has no public API,
  so automation is not possible. Requires active subscription to activate anyway.
  InvenTree covers part numbering, revision recording, and BOM -- sufficient for current needs.

## Done
- v1.0.0 -- Encrypted settings panel, push revision, fetch part data
