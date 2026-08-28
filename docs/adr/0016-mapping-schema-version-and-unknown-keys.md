# Mapping Schema Version and Unknown Keys

## Context

The **Property Mapping** JSON file includes a `SchemaVersion` marker. Older files may be missing newer fields; newer or future files may contain fields the current add-in does not yet understand.

## Decision

- Do not silently overwrite the file's `SchemaVersion` in memory. The value returned with the **Property Mapping** is the one read from the file.
- Fill missing known fields with defaults when an older `SchemaVersion` is loaded, so the add-in continues to work.
- Preserve unknown JSON keys from newer/future files so they round-trip on save and a future version can still read them.
- Show a warning in both the **Settings** window and the **Task Pane** when the file's `SchemaVersion` does not match the add-in's current version.
- Only rewrite the **Property Mapping** file on an explicit save.

## Consequences

- The schema-mismatch warning in the **Settings** window and the **Task Pane** becomes reachable.
- Engineers are notified when a **Property Mapping** file is older or newer, but they are not blocked from using the add-in.
- Unknown keys from future versions are not lost when an older add-in opens and saves the file.
