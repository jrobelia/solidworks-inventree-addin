---
name: pencil-mockups
description: Read, screenshot, and edit the UI mockups in docs/sw-addin-layout.pen via the Pencil MCP server. Use whenever a task touches the .pen file — reviewing design mocks, creating revised copies of windows, or checking whether shipped XAML matches the design source. The Pencil tool API has real quirks (inserted nodes park at wrong positions, bounds lag); read this before editing.
---

# Working with docs/sw-addin-layout.pen

`docs/sw-addin-layout.pen` is the design source of truth for the add-in's windows. `docs/agents/design-language.md` is a distillation of it — when they disagree, the .pen file shows the original intent (e.g., banded section headers on dialogs, which the shipped Settings window dropped).

## Access

All operations go through the `pencil` MCP server's `execute` tool:

```json
{ "filePath": "C:\\SoftwareProjects\\Solidworks Inventree Add-In\\docs\\sw-addin-layout.pen", "input": "<JS code>" }
```

Core functions: `Get(id, {depth})`, `Print`, `Copy(id, parentId)`, `Insert(parentId, props)`, `Update(id, props)`, `Delete(id)`, `Move(id, parentId, index)`, `FindEmptySpace({width,height,direction,nodeId,padding})`, `TakeScreenshot([ids])`. `Get` visits children with `(n, ctx)` — `ctx.bounds` gives computed layout bounds, `ctx.index` the sibling index.

## The golden rule: screenshots are ground truth

`ctx.bounds` and node properties routinely disagree with what actually renders — computed bounds lag behind `Update` calls, and freshly inserted nodes report positions they don't paint at. Always finish an edit with `TakeScreenshot` on the frame you changed. Verify no clipping, no collapsed elements, no overflow.

## Inserts park ~50px off, then decay — verify by screenshot

Freshly `Insert`ed nodes initially paint ~50px away from their stored position (sometimes fully clipped off-frame). The offset **decays to zero** on the next solver pass (seconds to a reconnect) — so the fix is: `Update` the node's x/y to the *real target* (not a compensated value), then wait/retry and re-screenshot. If you stored a compensated value (`target - 50`), the node lands `target - 50` after the decay — normalize stored values once bounds report correctly.

`Move` does not re-trigger layout. `Copy` is reliable: duplicated nodes keep correct positions and repaint immediately — prefer copying an existing canonical node (section header, status strip, button) over inserting new structure when possible.

Mid-list insertion without `Move`: delete the tail siblings, append the new node plus re-copied originals — append order becomes visual order. Text nodes auto-grow past their `width` — insert a manual `\n` to wrap.

## Flaky connection

`Failed to connect to MCP server 'pencil'` happens often. Retry after 10-25s. Reconnects seem to force a layout re-solve — a stuck element may correct itself after a disconnect, which can mask whether your fix worked. Re-screenshot after any reconnect.

## Disk writes lag the canvas — check before committing

MCP edits mutate the in-memory document; Pencil flushes to the `.pen` file on its own schedule. A `git commit` right after edits can capture a stale file — the tail of the work then shows up as an uncommitted diff on Pencil's next save. Before committing, `git status`/`git diff` the file, or nudge the app to save first.

## Schema notes

- `layout: vertical|horizontal|none`; children of `none` need explicit x/y and pixel sizes (`fill_container` warns outside flex).
- On flex children, `x`/`y` are ignored — position comes from index + alignment. If a flex child still parks wrong, set the parent to `layout:'none'` and pin children explicitly.
- Color tokens are `$color-*` variables. Use the exact names in the file (e.g., `$color-status-success`, `$color-accent-blue`, `$color-section-header`, `$color-warning`, `$color-muted`, `$color-border`). A wrong token name silently resolves to `#000000`.

## Canonical patterns already in the file

Copy these rather than recreating:

- Window chrome: 4px `$color-accent-blue` top stripe.
- Section/dialog title: 3px blue left stripe + `$color-section-header` grey band, ~26px tall, 12px/600 title.
- Status bar: 4px severity stripe + status text (see the v2 mocks' `statusStrip` frames).
- `Style Reference` frame holds the palette and type ramp.
