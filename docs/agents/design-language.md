# UI design language

The add-in's visual vocabulary — the patterns reused whenever a change touches `SwInventreeAddin/UI/*.xaml` or adds anything the engineer can see (a control, a grouped section, a status surface, a dialog). The enforceable rules live in `coding-standards.md` `## UI Design Language`; this doc is the vocabulary they cite. Find the matching pattern here before inventing a new one — when none fits, say so in the PR so the new pattern can be reviewed on its own merits.

Tokens are defined in `SwInventreeAddin/UI/DesignTokens.xaml` — this doc says when to reach for each one.

## Surfaces

- **Task Pane** (`TaskPaneView.xaml`) — the persistent SolidWorks side panel. Bottom-docked status bar; scrollable content stacked in sections.
- **Settings window** (`SettingsWindow.xaml`) — modal dialog; Server Connection card + credential form, Property Mappings, BOM keyword, footer action row.
- **Dialogs** (`BomCompareWindow`, `CreatePartWindow`, `PropertyMappingEditorWindow`, `MessageDialog`, `PushRevisionConfirmDialog`, `BomTableMissingDialog`, `ImageCropWindow`) — owned, centered pop-ups for focused tasks and prompts.

## Chrome — every window

- A 4px `BrushAccentBlue` accent stripe runs across the top of every window.
- Dialogs are `SizeToContent="Height"`, `ResizeMode="NoResize"`, `WindowStartupLocation="Manual"`.
- Centering and ownership come from `WindowCentering.Attach(this, owner)` — top-level windows are owned by the SolidWorks window handle, child dialogs by the window that opened them (ADR-0015).
- Message-box-style prompts go through `MessageDialog` — never `MessageBox.Show` (it can't center on the owner; ADR-0015).

## Typography and text roles

- `FontBody` is Segoe UI; `FontSizeBody` is 12; `FontSizeHeading` is 13.
- Primary text uses `BrushForeground`; help text uses `BrushSubtle` in a wrapped `TextBlock` directly under the field it explains.
- Field labels use `FieldLabelStyle`; comparison-grid column headers use `ColumnHeaderStyle`.

## Sections and containers

- **Task Pane section header** — a grey band (`BrushSectionHeader`, `Padding="6,4"`) with a 3px `BrushAccentBlue` stripe down its left edge and a SemiBold `FontSizeHeading` title.
- **Settings section header** — a plain SemiBold `BrushForeground` line; sections are separated by `Separator` + `BrushBorder` (`Margin="0,14,0,0"`).
- **Card** — a `Border` with `BrushSectionHeader` background, `BrushBorder` 1px, `Padding="10,8"`, `Margin="0,8,0,0"`, headed by a SemiBold label. The card is the *summary* surface — a saved-state digest like the Server Connection status card. Grey marks "read this"; it does not mark "edit here".
- **Bordered group** — same chrome (`BrushBorder` 1px, `Padding="10,8"`, `Margin="0,8,0,0"`, SemiBold label) but `Background="White"` — the canvas color. Use it to fence one logical *choice or edit region* (the credential group — username+password OR API key). White keeps it distinct from a grey summary card when the two stack, and matches the field-fill convention: interactive surfaces are white, chrome is grey.

## Fields and input

- `SWFieldStyle` (TextBox, 28px) and `SWPasswordBoxStyle` — white background, `BrushBorder` 1px, a `BrushAccentBlue` 2px focus ring.
- `InvenTreeFieldStyle` — cream `BrushInventreeField`, read-only. Reserved for values that came *from* InvenTree — the color itself signals "server data, not editable".
- Spacing tokens: `PanelPadding` (Task Pane content margin), `RowSpacing` between stacked controls, `SectionSpacing` between sections.

## Buttons and icons

- `PrimaryButtonStyle` (InvenTree blue) — exactly one per dialog: the commit action (`Save`, `IsDefault`).
- `SecondaryButtonStyle` (chrome grey) — everything else. 28px normally, 24px inside card toolbars.
- Icon pattern: label `TextBlock` followed by a Segoe MDL2 glyph `TextBlock` at `FontSize="13"`, `Margin="6,0,0,0"`. The glyph map lives in ADR-0009.
- Button rows use a `Grid` with `*` / `8` / `Auto` columns — a status bar or stretchy content on the left, buttons on the right separated by 8px columns.

## Status — color means severity

- The severity palette is `BrushStatusSuccess` / `BrushStatusWarning` / `BrushStatusError` / `BrushStatusNone`, plus `BrushStatusNotTested` (hollow-dot grey) and `BrushAccentBlue` (configured, unprobed).
- **Status bar** — `StatusBarControl`: a 32px strip with a 4px severity stripe and selectable read-only text (`StatusBarTextStyle`). The stripe carries the severity, the text carries the message. Bars are scoped — each reports its own scope's last action (the Server Connection bar reports connection actions; the footer bar reports Apply/Save).
- **Status dot** — the 10px ellipse on the Settings status card. Filled severity color for probed states; hollow (`Transparent` fill, `BrushStatusNotTested` stroke) for not-tested; `BrushAccentBlue` for configured-but-unprobed.
- The Task Pane's own status bar follows the same stripe + severity-icon convention, docked to the bottom.

## Reference material

- `docs/screenshots/` — screenshots of the current UI.
- `docs/sw-addin-layout.pen` — layout sketches.
- ADRs with UI decisions: 0009 (icon font + glyph map), 0010 (button spacing), 0015 (window centering), 0018 (mapping status), 0022 superseded by 0023 (Settings credential state and disclosure).

## Known drift

- `TaskPaneView.xaml` carries hardcoded greys — `#FFF0F0F0` status bar background, `#FFD0D0D0` border, `#FFD7D7D7` separator — that duplicate `BrushSectionHeader` / `BrushGridLine` values. Use the token in new work; treat the literals as drift to clean up when the file is touched anyway.
- `TaskPaneView.xaml` also defines a local `StatusTextBoxStyle` that predates the shared `StatusBarTextStyle`. New status bars use `StatusBarControl`.
