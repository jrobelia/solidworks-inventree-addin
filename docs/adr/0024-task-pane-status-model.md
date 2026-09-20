# Task Pane Status Model — Persistence Classes and Arbitration

## Context

The **Task Pane** status strip is a single-slot surface written through one choke point (`TaskPaneViewModel.SetStatus`, ~59 call sites) carrying three message classes: guidance hints (document/config state), transient action lifecycle ("Fetching…" → result), and persistent health facts (**Mapping Health**, and under #241 the last-known connection verdict). Under last-writer-wins, any transient silently erased a persistent fact until the next refresh — observed when an action result overwrote the mapping-health reading (#271).

Issue #271 asked which model to adopt: spatial partitioning (VS/SolidWorks-style slots per producer) or priority arbitration (one slot, most-important-first). `docs/research/status-surface-hig.md` surveyed the primary sources.

## Decision

The Task Pane status strip follows the **single-line status-line family** — Qt `QStatusBar` and Eclipse `IStatusLineManager` — the widget class it already is. Not toasts, banners, notification centers, or partitioned bars.

- Producers write **Status Entries** into the status model; the strip displays a **projection** of the top-priority entry. Entries are never overwritten — suppressed entries are retained and re-emerge.
- Every entry has a **persistence class** and a **severity** (`StatusSeverity`):
  - **Transient** — action lifecycle, two phases: in-progress ("Fetching…", `None`) then terminal result (`Success` or `Error`). Reigns until the next action result or an invalidating state change (document switch, config change, mapping refresh), then decays.
  - **Persistent** — projections of domain facts (server configured, credential saved, last-probe connection verdict, Mapping Health, document/link state). Emitted only when attention is needed; healthy facts emit no entry, so the rest state is blank or a neutral hint.
- **Display rule** (Qt's Temporary-over-Normal): while a transient reigns it owns the strip at any severity — action feedback always lands. Otherwise the highest-severity persistent entry displays (`Error` > `Warning` > ambient/`None`). The displayed entry's severity owns the stripe color.
- **Error retention latch** (Eclipse): an `Error`-tier entry never decays — it persists in the model until the underlying condition is remedied. Retention, not display immunity: a transient's reign may temporarily outrank it, and the error re-emerges when the reign ends. The strip carries ambient-tier information only — popups own the critical tier (ADR-0018) — and display immunity would re-create the #265 failure (a Settings save outcome could never surface while a mapping error is latched).
- **Suppressed entries** are listed in the strip's tooltip (`StatusToolTip`, already wired) — "winner displays, losers retrievable" per EEMUA 191.
- **No timers** — wall-clock decay is a floating-toast pattern (4–10 s). An inline strip decays on event: replacement or resolution.
- **Scope**: this model governs the whole add-in's status *semantics* — every surface is a scoped projection of one model. The Task Pane strip is the aggregation surface; the Settings card and footer, dialog status bars, and the mapping section strip remain scoped to their own domain per ADR-0018. Where the projection lives in code is #89's design territory; this ADR rules semantics only.

## Considered Options

- **Spatial partitioning** (VS/SolidWorks slots) — rejected: the pane is too narrow for slots, and partitioning answers "whose message is this" when the actual problem was "which entry deserves the surface now."
- **Error display immunity** (Eclipse's latch as a display property) — rejected: it would hide all action feedback while any error is active, and re-create the #265 unreadable-save-outcome failure.
- **Wall-clock decay** — rejected: a mobile/toast pattern that would re-create #265's vanishing-outcome bug.

## Consequences

- `SetStatus`'s call sites become producers of typed **Status Entries**, not writers of a shared text field.
- "Settings saved — connection successful" (the #265 direction folded from #252) lands as a **Transient** entry — readable on the Task Pane after the dialog closes.
- #241's connection surfacing emits `Error`/`Warning` entries from the last-known probe verdict only — no periodic probing; healthy or never-probed emits nothing.
- The scattered `SetStatus("", None)` clears are replaced by event-driven decay.
- The producer map — which domain emits which entries — is enumerated in #271; implementation lands through #89's adapter.
