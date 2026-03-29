# Decision Log

Append-only. One entry per non-obvious decision. Never delete old entries.

## 2026-02-26 -- DPAPI over AES key file for credential storage
Settings encrypted with Windows DPAPI (user scope). No key management needed --
Windows ties decryption to the logged-in user account. Trade-off: credentials
don't survive a Windows account migration, but that's rare and re-entering
a URL + API key is trivial.

## 2026-02-26 -- ConfigureAwait(false) + RunOnUiThread instead of Task.Run
SolidWorks runs on an STA thread. Task.Run forced continuations onto thread
pool threads, which deadlocked NUnit's STA runner and risked COM violations.
Direct await with ConfigureAwait(false) lets the HTTP call run on the thread
pool naturally, then RunOnUiThread marshals back via Invoke.

## 2026-02-26 -- Removed JsonFileConfigProvider entirely
The encrypted settings panel replaces the JSON file approach. Keeping both
alive would mean two code paths and user confusion. Clean cut.

## 2026-02-26 -- Installer versioning from git tags
Package.ps1 runs `git describe --tags --always` to name the zip and stamp
a version.txt inside it. No manual version bumping required.

## 2026-02-27 -- Test gap: spec items that touch the UI shell need explicit tests
During image push feature build, two spec items had no covering tests written
in Stage 5, so they survived to manual verify broken: (1) the "Include image"
checkbox in the Push Revision confirmation dialog, (2) `SwAddin` wiring the
real `SwViewportCaptureService` into the `TaskPaneControl` constructor.
Rule: any time the spec says "dialog shows X" or "SwAddin wires Y", write a
failing test for it in Stage 5 before writing any production code. If a
dialog-level behaviour is hard to unit-test, call it out explicitly in the
manual-verify checklist rather than leaving it implicit.

## 2026-02-27 -- PNG over JPEG for InvenTree part thumbnails
PNG chosen for clean edges when images are shrunk or converted to B&W for
label printing. 800x800 max PNG is ~50-150 KB -- negligible difference vs
JPEG at this size.

## 2026-02-27 -- Registration is one-time per DLL path
`RegAsm /codebase` writes the DLL path to the Windows registry once.
SolidWorks reads that path on every startup. Re-registration is only needed
if the DLL path changes (e.g. moving the project folder). Rebuilding in place
does not require re-registration.

## 2026-02-27 -- SaveBMP over screen capture for viewport images
Original approach used Win32 `EnumChildWindows` to find the SolidWorks viewport
window and `BitBlt` to screen-capture it. Fragile: heuristic child-window
matching, captured overlapping windows, wrong region if panels were docked
differently. Replaced with `IModelDoc2.SaveBMP(path, 0, 0)` -- official API
that renders the viewport to a temp BMP file at current dimensions. Clean,
reliable, no P/Invoke.

## 2026-02-27 -- White border on buttons instead of spacer panels
Attempted invisible spacer Panel between buttons for visual separation.
WinForms AutoSize layout collapsed the spacer repeatedly. Simpler fix:
1px white border + 2px vertical margin on every button via `MakeButton`.
Stands out against the blue background and requires no extra controls.

## [2026-03-12] Placeholder text in PropertyMappingEditorWindow deferred

The task 0c spec called for italic placeholder text (e.g. "(property name)") in the five
mapping TextBoxes in `PropertyMappingEditorWindow`. WPF TextBox does not have a built-in
placeholder property — it requires a trigger-based Style or AdornerDecorator pattern.

Decided to defer: the editor is fully functional without it, and the fields pre-fill from
`GetMapping()` so they are never visually empty on first open (defaults are written on
first launch). Placeholder styling can be added as a cosmetic polish task when the full
mapping UI is revisited.

## [2026-03-27] Segoe MDL2 Assets for all UI icons

Segoe MDL2 Assets chosen as the icon font across all WPF windows. It ships
with Windows 10/11 — no bundling, no NuGet package, no font file in Resources.
Rendered as `FontFamily="Segoe MDL2 Assets"` TextBlocks inside Button
StackPanels. Key glyphs in use: E713 settings gear, E896 download/load,
E76C forward arrow (push →), E76B back arrow (apply ←), E74E save floppy,
E711 cancel ✕, E73E check ✓, E703 refresh/test, E8B7 folder/browse, E70F
edit pencil, E783 error.

Rejected Material Symbols and Segoe Fluent Icons — MDL2 is the widest-supported
option on Windows 10 installs without bundling.

## [2026-03-27] WPF focus ring pattern for text inputs

All editable TextBox and PasswordBox controls use an `IsFocused` trigger in
their Style (`SWFieldStyle` / `SWPasswordBoxStyle`) that sets
`BorderBrush=BrushAccentBlue` and `BorderThickness=2` when focused.
Read-only InvenTree fields use `InvenTreeFieldStyle` with a lock tooltip
(`ToolTip="Read-only — value from InvenTree"`) but no focus ring.

## [2026-03-12] SettingsWindow refreshes mapping status after editor closes (plan deviation)

The task 0c plan (line 145) stated "the status bar does not need to refresh when the editor closes."
The implementation calls `RefreshMappingStatus()` in `SettingsWindow.EditMappings_Click` after
`editor.ShowDialog()` returns.

This is a deliberate improvement: if the user edits and saves mappings, the status bar correctly
transitions (e.g. "No mappings configured" → "Using local mappings") without requiring a
Settings dialog close-and-reopen. The plan was conservative; the implementation is better.

## [2026-03-28] PartCreated handler delegates to ApplyFetchedPart, not FetchPartAsync

After a successful part create, the task pane needs to enter POPULATED state (previews filled,
Apply/Push buttons enabled). The first attempt called `FetchPartAsync()` from the `PartCreated`
handler. This caused a race: SolidWorks fires `ActiveDocChangeNotify` when the dialog closes,
which called `LoadPartNumber()` → `ResetInvenTreeState()`, blowing away the state before or
after the async fetch completed.

Fix: introduced `ApplyFetchedPart(part, thumbBytes?)` as a private method — the single
authoritative place to enter POPULATED state. `FetchPartAsync` and the `PartCreated` handler
both call it. The handler sets state synchronously (part already in hand from `CreateAsync`),
so no async race is possible. `LoadPartNumber` guards against resetting if `_lastFetchedPart.Ipn`
already matches the doc IPN.

## [2026-03-28] Optional IPN field on Create Part dialog

Users without an InvenTree IPN-generation plugin, or who want a specific IPN, need a way to
provide one at create time. Added an optional "Part Number" field to `CreatePartWindow`.
When blank, the IPN key is omitted from the POST body entirely — InvenTree plugin or server
default handles assignment. When filled, it is sent as `"ipn"` in the POST body.
Never overrides server behaviour when left blank.

## [2026-03-29] PK match indicator added to task pane

The InvenTree PK row in the task pane now displays a `=` / `≠` match indicator,
consistent with Name, Description, Notes, and Revision rows. The PK value is
read-only on both sides (no Push button) but the indicator gives the user
visual confirmation that the SW custom property matches the fetched InvenTree PK.

## [2026-03-29] Local Mapping File section moved above Shared in Settings

Local Mapping File is the default path for all users. Shared Mapping File is
optional (team use). Presenting Local first matches the default selection state
and reduces confusion for single-user setups.

## [2026-03-28] GetMapping() has a first-run file-write side-effect

`PropertyMappingProvider.GetMapping()` writes default JSON to the local path on first call if
the file does not exist. This means opening the Settings dialog (which calls `RefreshMappingStatus()`
→ `GetMapping()` for schema version checking) silently creates the local mapping file.

As a result, the "No mappings configured" status in `SettingsWindow` is unreachable in practice
— the first Settings open always creates defaults and transitions to "Using local mappings."

Accepted as a known limitation for Milestone 1. Fix would require a `TryGetMapping()` interface
variant that does not have the first-run write side-effect. Deferring until the mapping lifecycle
(e.g. explicit "Reset to defaults") is designed properly.
