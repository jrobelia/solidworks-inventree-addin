# Property Mapping Health and Part Sync Lockout

## Context

The **Property Mapping** JSON file now drives all Part Sync and BOM Compare operations. Issues #133–#139 showed that the previous design — silently backfilling missing fields with defaults, letting each window compute its own status, and allowing Apply/Push with an older or invalid mapping — produced inconsistent state, lost unknown keys, and allowed writes based on an ambiguous configuration.

## Decision

- Introduce a `MappingHealth` result returned by `IPropertyMappingProvider.GetMappingResult()`. The result contains both the loaded `PropertyMappingConfig` and the `MappingHealth`.
- `MappingHealth` has three states:
  - `Healthy` — current **Mapping Schema Version**, valid mappings, no duplicates, readable. All Part Sync actions allowed.
  - `NeedsUpgrade` — older **Mapping Schema Version**. The editor opens with new fields blank and current default placeholders. **Fetch** is allowed, but **Apply**, **Push**, **Create Part**, and **BOM Compare** are locked until the file is saved with the current schema.
  - `Invalid` — corrupt, locked, missing, unreadable, or duplicate SolidWorks Document Property names. No Part Sync, including **Fetch**, and the editor opens read-only.
- Do not silently backfill missing new fields at runtime. The editor is the only place new fields are filled, and only when the engineer saves.
- Preserve unknown top-level JSON keys through the editor so future add-in versions and hand-edited files do not lose data.
- The editor works on a draft copy. Save validates and writes; if save fails or the user cancels, the draft is discarded and the UI reverts.
- For a shared read-only **Property Mapping** with an older **Mapping Schema Version**, the editor offers a **Copy to local** button and instructs the engineer to switch to Local in Settings to edit.
- The editor exposes **BOM Column Aliases** for the four fields we currently use: IPN, Qty, Reference, Note. Aliases are comma-separated and validated before save.
- Duplicate non-blank SolidWorks Document Property names in the same mapping make the mapping `Invalid`.

## Lessons from PR #131

- **Catch mapping errors in one place, not in every UI call site.** Catching `InvalidOperationException` around multiple calls to `GetMapping()` and `RefreshCurrentProperties()` scattered the error handling and made it easy for one path to clear a warning that another path had just set.
- **Do not backfill missing fields with defaults at runtime.** Backfilling `BomColumn*` defaults made the mapping look configured when the file was missing them, so the editor could not tell the engineer which fields were new.
- **Do not mutate the runtime config object and use it as the editor’s draft.** Mutating the in-memory `PropertyMappingConfig` in the provider meant the editor had no clean draft to save or discard.
- **The add-in’s own writes must not trigger a full Task Pane refresh.** Document-property-changed events caused by `Apply` were handled the same as user edits, which cleared status and could reset the part session.
- **Tooltip tests must assert the visible text, not the control type.** Tests that only verified the `ToolTip` content was a `TextBlock` passed while the UI rendered the type name, so the rendered text must be asserted through the public UI seam.

## Consequences

- **Settings** and the **Task Pane** share one `MappingHealth` result and stay in sync.
- Part Sync cannot run with an ambiguous or outdated mapping, preventing silent data corruption.
- Older mapping files are migrated by opening the editor, filling the new fields, and saving — not by silent defaults.
- Unknown keys from newer/future versions round-trip.
- A shared older mapping does not block a single engineer; they can copy to local and upgrade.
- This supersedes ADR-0016 for the handling of missing fields and status. The unknown-key preservation from ADR-0016 remains in effect.
