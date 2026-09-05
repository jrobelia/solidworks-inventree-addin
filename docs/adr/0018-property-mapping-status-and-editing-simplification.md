# Property Mapping Status and Editing Simplification

## Context

During QA of PR #156 (`build/issue-146`) and the related redesign in ADR-0017, four issues surfaced about the same workflow: how the **Settings** window and **Task Pane** communicate mapping health, how the user edits a **Property Mapping**, and how the add-in handles a non-`Healthy` mapping.

- **#164** — `Fetch` with a `NeedsUpgrade` mapping cleared the mapping-health warning.
- **#165** — the **Settings** `Copy to local` button added complexity without a clear role.
- **#167** — the **Settings** and **Task Pane** status locations, message routing, and visual treatment were not clearly defined.
- **#166** — the `Create Part` workflow should not block duplicate **IPN** client-side.

This ADR supersedes the status, Copy-to-local, and Part-Sync-gating portions of ADR-0017. The core health model (`Healthy` / `NeedsUpgrade` / `NewerSchema` / `Invalid`), unknown-key round-trip, no-runtime-backfill, and duplicate-name validation from ADR-0017 remain in effect.

## Decision

- Only a `Healthy` **Property Mapping** allows any Part Sync action, including **Fetch**.
  - `NeedsUpgrade` and `NewerSchema` now block all Part Sync, including **Fetch**, until the file is saved with the supported schema or the add-in is upgraded.
  - `Invalid` continues to block all Part Sync, including **Fetch**.
- The `MappingHealth` status message is shown in both **Settings** and the **Task Pane**, using the same source from `IPropertyMappingProvider.GetMappingResult()`.
  - The base message is source-independent.
  - The status wording uses **"Property Mapping"** and **"Property Mapping Schema"** from `CONTEXT.md`.
- The **Settings** window has one status area per section, using the same colored-stripe + selectable message visual pattern as the **Task Pane**.
  - Each status bar is always visible and positioned to the left of the action button it reports on:
    - Connection status is left of **Test Connection**.
    - Mapping status is left of **Edit Mappings**.
    - Apply/Save status is left of the **Apply** / **Save** / **Cancel** buttons.
  - The visible status text and the hover tooltip carry the same full text (state plus actionable detail). The tooltip relies on the global wrapping `ToolTip` style, so long messages are readable. The status text uses a read-only, selectable `TextBox` so it can be copied.
- The **Settings** window no longer offers **Copy to local**.
  - The single **Edit Mappings** button is right-aligned in the same row as the mapping status bar, to the right of the status text.
  - Its label reflects the selected source: **"Edit Local Mappings"** or **"Edit Shared Mappings"**.
  - It is enabled only for `Healthy` and `NeedsUpgrade`.
  - It is disabled for `Invalid` and `NewerSchema`.
- The mapping editor opens and edits the resolved mapping file: the shared file when shared is selected and exists, otherwise the local file.
  - `IPropertyMappingProvider.SaveMapping` writes to the resolved file, not always the local path.
  - `IPropertyMappingProvider.IsReadOnly` is removed; `IPropertyMappingProvider.CopyToLocal` is removed.
  - If the resolved file is read-only at the OS level, the editor is still editable, but **Save** catches `UnauthorizedAccessException` / `IOException` and shows a clear, actionable message.
- The mapping editor no longer has a shared-file read-only banner. It always opens editable when the **Edit Mappings** button is enabled.
- Blank or missing individual mapping values are allowed. A mapping is `Invalid` only for duplicate SolidWorks Document Property names, a missing or unreadable file, an unparseable schema version, or an I/O error.
- The mapping editor window receives a styling cleanup: a section title for the first block ("Property Mappings"), consistent separators, matching column headers for the BOM Column Aliases table, a separator above the **BOM Column Aliases** section, and a status-bar-style validation status bar.
  - The editor's validation status bar is always visible, sits to the left of **Save** / **Cancel**, and uses the same read-only, selectable `TextBox` as the Settings status bars.
  - When there is no validation error the bar is grey (`BrushStatusNone`) with the message "No validation errors."; on a validation or save failure it turns red (`BrushStatusError`) and shows the error text. The hover tooltip matches the visible text.
  - The **BOM Column Aliases** subtitle reads: "Enter comma-separated SolidWorks BOM table column headers for each InvenTree field for assembly BOM comparison."

## Mapping-health messages

The same source text is used by Settings, the Task Pane, and the mapping editor, but the presentation differs:

- The **Settings** window and **mapping editor** status bars show the full message and use the same full message as the hover tooltip. The full message is exposed by `MappingResult.FullStatusMessage`.
- The **Task Pane** shows the base message in its status area and the actionable detail in the tooltip.

| State | Task Pane status / Settings base text | Tooltip detail | Full message (Settings / editor) |
|-------|--------------------------------------|----------------|----------------------------------|
| `Healthy` | "The Property Mapping file is up to date and valid." | — | "The Property Mapping file is up to date and valid." |
| `NeedsUpgrade` | "The Property Mapping Schema is out of date." | "Edit the Property Mapping and save to enable Part Sync." | "The Property Mapping Schema is out of date. Edit the Property Mapping and save to enable Part Sync." |
| `NewerSchema` | "The Property Mapping Schema is newer than this add-in." | "Upgrade the add-in to enable Part Sync." | "The Property Mapping Schema is newer than this add-in. Upgrade the add-in to enable Part Sync." |
| `Invalid` | "The Property Mapping file is invalid." | "{detail}. Fix the file, replace it, or choose a different mapping source in Settings." | "The Property Mapping file is invalid. {detail}. Fix the file, replace it, or choose a different mapping source in Settings." |

## Consequences

- `MappingResult.CanFetch` changes to `Health == MappingHealth.Healthy`.
- `TaskPaneViewModel.RefreshStatus` is the single source for mapping-health status in the Task Pane; `FetchPartAsync` no longer clears it on success.
- `SettingsWindow` no longer shows a `CopyToLocalButton`; `EditMappingsButton` uses the resolved source and is disabled for `Invalid` / `NewerSchema`.
- `IPropertyMappingProvider` loses `IsReadOnly` and `CopyToLocal`; `SaveMapping` resolves its own target path.
- `MappingEditorViewModel` no longer uses `IsReadOnly` from the provider; the editor is editable whenever the button enables it.
- `MappingResult` exposes `FullStatusMessage` to combine the base state text with the actionable detail for status bars that show the same full text and tooltip.
- `DesignTokens.xaml` exposes a shared `StatusBarTextStyle` for read-only, selectable status text; `SettingsWindow` and `PropertyMappingEditorWindow` use it for consistent status-bar presentation.
- `MappingEditorViewModel` exposes `StatusMessage` and `StatusSeverity` so the editor status bar follows the same always-visible, grey-by-default, red-on-error pattern as the Settings status bars.
- Tests must be updated or removed for `CopyToLocal` and for the previous `NeedsUpgrade` / `NewerSchema` `Fetch` behavior.
- `CONTEXT.md` and `docs/adr/0017-...` are superseded for status, Copy-to-local, and Part-Sync gating by this ADR.
