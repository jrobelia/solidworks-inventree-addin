# SolidWorks InvenTree Add-In

A SolidWorks add-in that bridges SolidWorks parts and assemblies with an InvenTree inventory server.

## Language

**IPN** (InvenTree Part Number):
The string that uniquely identifies a part in InvenTree and links it to a SolidWorks document via a custom property.
_Avoid_: part number, PN

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

**Part Sync**:
The workflow of fetching an InvenTree part by IPN, comparing its fields against SolidWorks Document Properties, and applying or pushing values in either direction.
_Avoid_: sync, update, comparison

**BOM Compare**:
The assembly-level workflow: load the SolidWorks BOM table and the InvenTree BOM, diff them, and selectively push lines.
_Avoid_: BOM sync, BOM check

**BOM Diff State**:
The per-line classification result of a BOM Compare: Match / New / Conflict / InvenTreeOnly / NoIpn / IpnNotFound / Ambiguous.

**Viewport Capture**:
Rendering the active SolidWorks 3D viewport to an image file for upload to InvenTree as a part thumbnail.

## Relationships

- An **IPN** links exactly one SolidWorks document to one InvenTree part (duplicate IPNs are a data error; Part Sync resolves them via revision matching)
- A **Property Mapping** governs which **SolidWorks Document Properties** are read and written during **Part Sync**
- A **BOM Compare** requires the Task Pane to be in POPULATED state (IPN resolved, InvenTree part fetched)
- **BOM Diff State** Conflict and New lines are user-selectable for pushing; InvenTreeOnly lines are never touched

## Example dialogue

> **Dev:** "When the engineer clicks Compare BOM, do we re-fetch from InvenTree?"
> **Domain expert:** "Only if the Task Pane isn't POPULATED — if we already have the InvenTree PK in memory from a previous Part Sync, we use it directly and skip the fetch."

## Flagged ambiguities

- "sync" is used loosely but means different things: **Part Sync** (field comparison + apply/push) vs **BOM Compare** (BOM diff + push). Use the specific term.
