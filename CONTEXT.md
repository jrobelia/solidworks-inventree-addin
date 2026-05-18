# SolidWorks InvenTree Add-In

A SolidWorks add-in that bridges SolidWorks parts and assemblies with an InvenTree inventory server.

## Language

**IPN** (InvenTree Part Number):
The user-supplied string that identifies a part in InvenTree; stored as a SolidWorks Document Property and used to look up the part at fetch time.
_Avoid_: part number, PN

**InvenTree Part PK**:
The server-assigned integer primary key for an InvenTree part. Stored as a SolidWorks Document Property in two cases: automatically on Create Part, or manually when the engineer clicks "Apply to SW Doc" on the PK field after a Fetch. Used to address the part directly in subsequent API calls without an IPN lookup. Distinct from PKs on other InvenTree objects (supplier parts, purchase orders, build orders, etc.).
_Avoid_: PK, InvenTree PK

**SolidWorks Document Property**:
A SolidWorks custom property on a part or assembly document.
_Avoid_: custom property, SW property

**Property Mapping**:
The user-configurable JSON file that maps InvenTree field names (name, notes, revision, description, IPN) to corresponding SolidWorks Document Property names.
_Avoid_: field mapping, property config

**Task Pane**:
The persistent SolidWorks side panel that hosts the add-in UI.
_Avoid_: sidebar, panel, control

**Task Pane State**:
One of four states: EMPTY (no document open), UNLINKED (document open but no IPN), LINKED (IPN present, not yet fetched), POPULATED (InvenTree data in hand).

**Fetch**:
Retrieve an InvenTree part by IPN (or InvenTree Part PK) from the server and display its field values as a preview in the Task Pane. Does not modify SolidWorks Document Properties.
_Avoid_: load, sync, pull

**Apply** (Apply to SW Doc):
Copy the fetched InvenTree preview values into the SolidWorks Document Properties of the active document. Direction: InvenTree → SW. Labelled "Apply to SW Doc" in the UI.
_Avoid_: write, import, save to document

**Push**:
Send the current SolidWorks Document Property values to InvenTree, updating the server record. Direction: SW → InvenTree.
_Avoid_: upload, sync, export

**Part Sync**:
The umbrella session covering Fetch, then one or more Apply or Push operations for a single part. Not a single button — it describes the engineer's overall workflow at the Task Pane.
_Avoid_: sync, update, comparison

**BOM Compare**:
The assembly-level workflow: load the SolidWorks BOM table and the InvenTree BOM, diff them, and selectively push lines.
_Avoid_: BOM sync, BOM check

**BOM Keyword**:
The word the engineer includes in their SolidWorks BOM feature name (e.g., "InvenTree BOM") so the add-in can identify which table to use during BOM Compare. Matched case-insensitively as a substring against feature names in the FeatureManager. User-configurable; defaults to `"inventree"`.
_Avoid_: BOM filter, table name, BOM template

**BOM Diff State**:
The per-line classification result of a BOM Compare: Match / New / Conflict / InvenTreeOnly / NoIpn / IpnNotFound / Ambiguous.

**Create Part**:
The workflow of creating a new InvenTree part record from the active SolidWorks document, then stamping the returned InvenTree Part PK back into the document's SolidWorks Document Properties.

**Viewport Capture**:
Rendering the active SolidWorks 3D viewport to an image file for upload to InvenTree as a part thumbnail.

**Document Type**:
The type of the active SolidWorks document: Part, Assembly, Drawing, or Unknown. Determines which workflows are available. Drawing is unsupported — the Task Pane shows a warning and disables all operations. BOM Compare is only available for Assembly documents.
_Avoid_: file type, SW type

## Relationships

- An **IPN** links exactly one SolidWorks document to one InvenTree part (duplicate IPNs are a data error; Part Sync resolves them via revision matching)
- A **Property Mapping** governs which **SolidWorks Document Properties** are read or written during **Fetch**, **Apply**, and **Push**
- **Fetch** transitions the Task Pane from LINKED → POPULATED; the **InvenTree Part PK** is shown as a preview but is only written to the document when the engineer explicitly applies it
- **Apply** and **Push** are only available when the Task Pane is POPULATED
- **Create Part** is available when the Task Pane is UNLINKED (document open, no IPN assigned); it automatically stamps the **InvenTree Part PK** into the document on success
- **BOM Compare** requires both POPULATED state and an Assembly **Document Type**
- The **BOM Keyword** selects which SolidWorks BOM table feeds a **BOM Compare**
- **BOM Diff State** Conflict and New lines are user-selectable for pushing; InvenTreeOnly lines are never touched

## Example dialogue

> **Dev:** "When the engineer clicks Compare BOM, do we re-fetch from InvenTree?"
> **Domain expert:** "Only if the Task Pane isn't POPULATED — if we already have the **InvenTree Part PK** in memory from a previous Part Sync, we use it directly and skip the fetch."

## Flagged ambiguities

- "sync" is used loosely but means different things: **Part Sync** (field comparison + apply/push) vs **BOM Compare** (BOM diff + push). Use the specific term.
- `BomCompareViewModel` previously used "Apply" (`ApplyAsync`, `ApplyEnabled`) for its SW → InvenTree BOM push operation — conflicting with **Apply** (InvenTree → SW). Resolved: renamed to `PushAsync` / `PushEnabled` / `IsPushing` to match the UI label "Push Selected to InvenTree" and the domain term.
