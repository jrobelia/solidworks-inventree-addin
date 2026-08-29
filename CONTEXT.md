# SolidWorks InvenTree Add-In

A SolidWorks add-in that bridges SolidWorks parts and assemblies with an InvenTree inventory server.

## Language

**IPN** (InvenTree Part Number):
The string that identifies a part in InvenTree; may be user-supplied or server-assigned (e.g., by an InvenTree plugin when the user leaves the IPN field blank during part creation). Stored as a SolidWorks Document Property and used to look up the part at fetch time.
_Avoid_: part number, PN

**InvenTree Part PK**:
The server-assigned integer primary key for an InvenTree part; stored as a SolidWorks Document Property so subsequent operations can address the part directly without an IPN lookup. Distinct from PKs on other InvenTree objects (supplier parts, purchase orders, build orders, etc.).
_Avoid_: PK, InvenTree PK

**SolidWorks Document Property**:
A SolidWorks custom property on a part or assembly document.
_Avoid_: custom property, SW property

**Property Mapping**:
The user-configurable JSON file that maps InvenTree field names (name, notes, revision, description, IPN) to corresponding SolidWorks Document Property names, and that maps SolidWorks BOM column headers to InvenTree BOM line item fields.
_Avoid_: field mapping, property config

**Mapping Schema Version**:
A version marker inside the Property Mapping JSON file. When the marker is older or newer than the add-in's current format, the **Settings** window and **Task Pane** show a warning, and **Mapping Health** may restrict Part Sync until the file is reviewed and saved.
_Avoid_: schema, mapping version

**Mapping Health**:
The add-in's evaluation of whether the current **Property Mapping** can be used. `Healthy` allows all Part Sync actions; `NeedsUpgrade` allows **Fetch** but blocks **Apply**, **Push**, **Create Part**, and **BOM Compare** until the file is saved with the current **Mapping Schema Version**; `Invalid` blocks all Part Sync, including **Fetch**.
_Avoid_: mapping status, mapping state

**Task Pane**:
The persistent SolidWorks side panel that hosts the add-in UI.
_Avoid_: sidebar, panel, control

**Task Pane State**:
One of four states: EMPTY (no document open), UNLINKED (document open, no IPN and no InvenTree Part PK), LINKED (IPN or InvenTree Part PK present, not yet fetched), POPULATED (InvenTree data in hand).

**Fetch**:
Retrieve an InvenTree part by IPN (or InvenTree Part PK) from the server and display its field values as a preview in the Task Pane.
_Avoid_: load, sync, pull

**Apply** (Apply to SW Doc):
Write fetched InvenTree preview values into the SolidWorks Document Properties of the active document. Direction: InvenTree → SW. Labelled "Apply to SW Doc" in the UI.
_Avoid_: write, import, save to document

**Push**:
Send SolidWorks Document Property values to InvenTree, updating the server record. Direction: SW → InvenTree.
_Avoid_: upload, sync, export

**Part Sync**:
The umbrella term for a Fetch followed by one or more Apply or Push operations on a single part.
_Avoid_: sync, update, comparison

**BOM Compare**:
The assembly-level workflow: diff the SolidWorks BOM table against the InvenTree BOM and selectively push lines.
_Avoid_: BOM sync, BOM check

**BOM Keyword**:
A user-configurable string the engineer includes in their SolidWorks BOM feature name (e.g., "InvenTree BOM") to identify which table to use during BOM Compare. Defaults to `"inventree"`.
_Avoid_: BOM filter, table name, BOM template

**BOM Column Alias**:
A comma-separated list of SolidWorks BOM column header names that the add-in treats as the same InvenTree BOM line item field (e.g. IPN, Qty, Reference, Note).
_Avoid_: BOM column mapping, BOM header

**BOM Diff State**:
The per-line classification result of a BOM Compare: Match / New / Conflict / InvenTreeOnly / NoIpn / IpnNotFound / Ambiguous.

**Create Part**:
The workflow of creating a new InvenTree part record from the active SolidWorks document.

**Viewport Capture**:
Rendering the active SolidWorks 3D viewport to an image file for upload to InvenTree as a part thumbnail.

**Document Type**:
The type of the active SolidWorks document: Part, Assembly, Drawing, or Unknown.
_Avoid_: file type, SW type

## Relationships

- An **IPN** links exactly one SolidWorks document to one InvenTree part
- A **Property Mapping** governs which **SolidWorks Document Properties** are read or written during **Fetch**, **Apply**, and **Push**, and which SolidWorks BOM column headers are recognized during **BOM Compare**
- An **InvenTree Part PK** is associated with exactly one InvenTree part and is distinct from the **IPN**
- **BOM Compare** operates on an Assembly **Document Type** and uses the **BOM Keyword** to locate the source table
- **Mapping Health** determines whether **Fetch**, **Apply**, **Push**, **Create Part**, and **BOM Compare** are allowed
- Each BOM line in a **BOM Compare** result carries exactly one **BOM Diff State**

## Example dialogue

> **Dev:** "When the engineer clicks Compare BOM, do we re-fetch from InvenTree?"
> **Domain expert:** "Only if the Task Pane isn't POPULATED — if we already have the **InvenTree Part PK** in memory from a previous Part Sync, we use it directly and skip the fetch."

## Flagged ambiguities

- "sync" is used loosely but means different things: **Part Sync** (field comparison + apply/push) vs **BOM Compare** (BOM diff + push). Use the specific term.
- `BomCompareViewModel` previously used "Apply" (`ApplyAsync`, `ApplyEnabled`) for its SW → InvenTree BOM push operation — conflicting with **Apply** (InvenTree → SW). Resolved: renamed to `PushAsync` / `PushEnabled` / `IsPushing` to match the UI label "Push Selected to InvenTree" and the domain term.
