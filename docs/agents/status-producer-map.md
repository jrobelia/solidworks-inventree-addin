# Status producer map

Every producer that writes an add-in status surface, classified under the ADR-0024 status model — which domain emits which **Status Entry**, with what persistence class and severity. Snapshot at `fdb16c2` (`milestone-3`, September 2026): line numbers rot as the files change; the producer, trigger, and classification outlive them.

Consumers: #90's characterization tests pin this surface; #271's implementation children re-platform each domain onto entries. Vocabulary is `CONTEXT.md` (**Status Entry**, **Transient Status**, **Persistent Status**); arbitration rules are ADR-0024 — this map records what writes today, not what the model prescribes.

## Task Pane strip

The aggregation surface: one slot behind `TaskPaneViewModel.SetStatus` (`StatusText` / `StatusSeverity` / `StatusToolTip`), last-writer-wins. ~45 call sites in `TaskPaneViewModel.cs` — since #92 the coordinator returns typed `PartSyncResult`s and the ViewModel maps them, so the former `PartThumbnailService` callback messages arrive as `SucceededWithWarning` diagnostics instead.

### Persistent — document and config facts (guidance hints)

Written imperatively at each site today; under the model they are projections of state, not written literals.

| Site | Trigger | Text | Severity |
| --- | --- | --- | --- |
| `TaskPaneViewModel.cs:489` | `LoadPartNumber`, Drawing active | "Drawings are not supported — open a part or assembly." | Warning |
| `:522` | `LoadPartNumber`, UNLINKED + no client | "No server configured — click ⚙ Settings to get started" | Warning |
| `:559` | `LoadPartNumber`, LINKED-by-PK + no client | same | Warning |
| `:584` | `LoadPartNumber`, LINKED-by-IPN + no client | same | Warning |
| `:699` | `ClearAll` + no client | same | Warning |
| `:705` | `ClearAll` + client | "Open a part or assembly in SolidWorks to get started." | None |
| `:904` | `FetchPartAsync`, blank IPN | same | None |

### Transient — Fetch lifecycle

| Site | Trigger | Text | Severity |
| --- | --- | --- | --- |
| `:823` | PK path, start | "Fetching from InvenTree…" | None (in-progress) |
| `:828` | PK path, client null | "No server configured — click ⚙ Settings…" | Warning |
| `:853` | PK path, request threw | `Error: {pkError.Message}` | Error |
| `:859` | PK path, part null | `No part found in InvenTree for PK: {_documentPk}` | Warning |
| `:879` | Link Mismatch declined | "Fetch cancelled — Link Mismatch." | Warning |
| `:908` | IPN path, start | "Fetching from InvenTree…" | None (in-progress) |
| `:913` | IPN path, client null | "No server configured — click ⚙ Settings…" | Warning |
| `:937` | IPN path, request threw | `Error: {fetchError.Message}` | Error |
| `:943` | IPN path, no results | `No part found in InvenTree for: {ipn}` | Warning |
| `:967` | duplicate IPNs, no revision match | `{parts.Count} parts share IPN ‘{ipn}’ but none match SW revision {revLabel}. Resolve in InvenTree.` | Error |
| `:977` | duplicate IPNs, several revision matches | `{parts.Count} parts share IPN ‘{ipn}’ and revision {revLabel}. Resolve duplicates in InvenTree.` | Error |

### Transient — Create Part result

| Site | Trigger | Text | Severity |
| --- | --- | --- | --- |
| `:777` | `PartCreated` handler, dialog closed | `IpnMismatchNotice` or "Part created in InvenTree." | Warning / Success |

### Transient — Apply lifecycle

Synchronous writes: terminal result only, no in-progress phase.

| Site | Trigger | Text | Severity |
| --- | --- | --- | --- |
| `:1026` | `ApplyNameToDocument` | "Name applied." | Success |
| `:1036` | `ApplyNotesToDocument` | "Notes applied." | Success |
| `:1046` | `ApplyDescriptionToDocument` | "Description applied." | Success |
| `:1056` | `ApplyPkToDocument` | "InvenTree PK applied." | Success |

### Transient — Push lifecycle

Each field push follows in-progress → terminal; `PushImageAsync`'s degraded outcomes arrive as `PartSyncOutcome.SucceededWithWarning` diagnostics from the coordinator (`PartThumbnailService` was deleted in #92 — same wording, now produced in `PartSyncCoordinator.PushImageAsync` and mapped by the ViewModel).

| Site | Trigger | Text | Severity |
| --- | --- | --- | --- |
| `TaskPaneViewModel.cs` | `PushRevisionToInventreeAsync`, part PK is 0 | "Error: cannot push revision — InvenTree part ID is missing." | Error |
| Push Revision | "Pushing revision to InvenTree…" / "Revision pushed to InvenTree." / `Error: {diagnostic}` | None / Success / Error |
| Push Name | "Pushing name…" / "Name pushed to InvenTree." / `Error: {diagnostic}` | None / Success / Error |
| Push Notes | "Pushing notes…" / "Notes pushed to InvenTree." / `Error: {diagnostic}` | None / Success / Error |
| Push Description | "Pushing description…" / "Description pushed to InvenTree." / `Error: {diagnostic}` | None / Success / Error |
| `PushImageAsync`, before upload | "Pushing image to InvenTree…" | None (in-progress) |
| `PushImageAsync`, upload failed | `Error: {diagnostic}` | Error |
| `PushImageAsync`, `SucceededWithWarning` — re-fetch returned null | "Image pushed, but the part could not be re-fetched for a preview." | Warning |
| `SucceededWithWarning` — no thumbnail URL | "Image pushed, but InvenTree did not return a thumbnail URL." | Warning |
| `SucceededWithWarning` — download returned null | "Image pushed, but the thumbnail could not be downloaded." | Warning |
| `SucceededWithWarning` — refresh threw | "Image pushed, but the thumbnail preview could not be refreshed." | Warning |
| `PushImageAsync`, success | "Image pushed to InvenTree." | Success |

### Persistent — Mapping Health

`RefreshStatus()` re-asserts the reading on every state change; it is the only producer that sets `StatusToolTip` (the tooltip duplicates the message today).

| Site | Trigger | Text | Severity |
| --- | --- | --- | --- |
| `:1351` | `MappingHealth.Invalid` | `MappingResult.FullStatusMessage` | Error |
| `:1355` | `MappingHealth.NeedsUpgrade` | same | Warning |
| `:1359` | `MappingHealth.NewerSchema` | same | Warning |

### Clears — manual decay

Every `SetStatus("", None)` is hand-rolled decay; ADR-0024 replaces all of them with event-driven decay.

| Site | Trigger | Decays what |
| --- | --- | --- |
| `:562` | LINKED-by-PK + client, session differs (`!sessionMatches`) | whatever reigned |
| `:591` | LINKED-by-IPN + client | whatever reigned |
| `:630` | `OnDocumentPropertyChanged`, user edit diverges | stale action result |
| `:895` | PK fetch success | the "Fetching…" in-progress phase — success writes blank |
| `:1002` | IPN fetch success | same |
| `:1365` | mapping health restored (`_mappingHealthWarningActive` latch) | the health entry |

## Scoped surfaces

ADR-0024's scope decision: every surface is a scoped projection of the one model; these stay scoped to their own domain per ADR-0018 and are listed at producer granularity rather than per call site.

| Surface | Producer | Writes | Class |
| --- | --- | --- | --- |
| Settings status card + dot | `SettingsViewModel.StatusCard` (`ServerConnectionStatus`) | saved config, credential, last-probe connection verdict | Persistent — card scope |
| Settings connection bar | `SettingsViewModel.SetConnectionStatus` → `ConnectionStatusBar` | "Testing connection…", probe results, Change-server outcomes | Transient — section scope |
| Settings mapping strip | `SettingsViewModel.MappingStatusText` → `MappingStatusBar` | `MappingResult.FullStatusMessage` | Persistent — section scope |
| Settings footer bar | `SettingsViewModel.SetActionStatus` → `ActionStatusBar` | "Saving settings…", Apply/Save outcomes | Transient — footer scope; the Save outcome re-homes to the Task Pane under the save-outcome child |
| Create Part dialog bar | `CreatePartViewModel.SetStatus` — 12 sites (`:282`, `:290`, `:295`, `:335`, `:361`, `:370`, `:384`, `:392`, `:407`, `:439`, `:453`, `:457`) | category loads, "Creating part…", IPN poll, validation and create errors; `:361` projects Mapping Health into the dialog | Transient — dialog scope |
| BOM Compare bar | `BomCompareViewModel.StatusText` — 6 writes (`:165`, `:180`, `:196`, `:200`, `:209`, `:295`) | "Loading…", "Pushing selected lines…", per-line result summary | Transient — dialog scope |
| Mapping editor bar | `MappingEditorViewModel.StatusMessage`/`StatusSeverity` | validation errors/warnings projected from the draft | Persistent — dialog scope |
| Popups (`MessageDialog`, `PushRevisionConfirmDialog`, `BomTableMissingDialog`, `ImageCropWindow`) | — | none — popups own the critical tier (ADR-0024), outside the strip's lanes | — |

`StatusBarControl.SetStatus` and the `SettingsWindow.xaml.cs` forwards are render adapters, not producers — they draw whatever a ViewModel hands them.

## What the map shows

- Six bare clears are the only decay mechanism; the model's event-driven decay replaces every one.
- Fetch success has no terminal string — it writes blank (`:895`, `:1002`). Whether POPULATED is itself the feedback or a "Fetch complete" result exists is the core ticket's call.
- "No server configured" is one fact written at six sites — including twice inside Fetch (`:828`, `:913`), where the literal overwrites the in-progress message. As a persistent entry it needs no mid-fetch write.
- `_mappingHealthWarningActive` is the existing retention latch — the precedent the Error-tier latch generalizes.
- Any terminal result silently erases the mapping-health reading until the next `RefreshStatus` — the persistent-vs-transient contention ADR-0024 removes.
