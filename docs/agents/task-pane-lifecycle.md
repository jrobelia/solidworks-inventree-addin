# Task Pane lifecycle

The Task Pane's state and lifecycle contract, locked down in #90 ahead of the
#91–#93 state moves. The five kinds below are characterized as observable
`TaskPaneViewModel` state by `TaskPaneLifecycleCharacterizationTests`
(`SwInventreeAddin.Tests/TaskPaneViewModelTests.cs`); the `#92` matrix is
recorded here as a document — it deliberately has no executable tests yet.

## Task Pane State kinds

The Task Pane is in exactly one of five states at any time:

| Kind | Invariants |
| --- | --- |
| EMPTY | No active document; no populated session. |
| UNSUPPORTED | Unsupported active document (currently Drawing); no populated session. |
| UNLINKED | Supported document, no IPN, no stamped InvenTree Part PK, no populated session. |
| LINKED | A supported document has an IPN or stamped InvenTree Part PK, but no session valid for the current document generation and identifiers. |
| POPULATED | Populated part/session belongs to the current active-document generation and identifiers. |

Busy, failure, and confirmation are presentation overlays, not kinds — a Fetch
in flight is still LINKED, and a failed Fetch stays in whatever kind the
document warrants.

## Lifecycle contract

- **Document generation.** Activating or changing the active document advances
  a document generation (or equivalent reliable identity). A generation
  represents an actual active-document change, not a SolidWorks callback
  count — duplicate `DocumentLoadNotify2` / `ActiveDocChangeNotify` callbacks
  for the same document are refreshes and do not advance it. A same-document
  property refresh (`OnDocumentPropertyChanged`, or a reload that re-reads
  identical identity stamps) does not advance it either.
- **Operation token.** Async work captures an opaque three-component token —
  document generation + coordinator lifecycle revision + request order —
  plus the relevant IPN / InvenTree Part PK. The coordinator lifecycle
  revision advances on client replacement (`UpdateClient`), Property Mapping
  replacement (`UpdateMapping`), and disposal/shutdown. Request order
  distinguishes overlapping requests within one generation + revision.
- **Stale completion.** A completion whose captured token no longer matches
  must not install a session and must not write SolidWorks Document
  Properties.
- **Cancellation is best effort.** `IInventreeClient` carries no
  `CancellationToken`s, so cancellation is *attempted* on document switch,
  close, client replacement, and shutdown — but token validation is the
  correctness mechanism, not cancellation.
- **Threading.** SolidWorks work follows STA capture → off-thread network
  work → STA validation/commit. `TaskPaneViewModel` captures
  `SynchronizationContext.Current` and the managed thread id at construction,
  network awaits use `ConfigureAwait(false)`, and commits marshal back through
  `RunOnUiThread` — a managed-thread-id check, never a context comparison
  (ADR-0002). The rule is testable without SolidWorks installed:
  `StubSynchronizationContext` counts `Send` calls and the deferred-completion
  stubs complete fetches off the captured thread.
- **Presentation.** Confirmations (Link Mismatch, duplicate IPN, missing
  properties) and status wording remain presentation concerns. Producer
  triggers and current wording are catalogued in
  [status-producer-map.md](status-producer-map.md). Today's strip is
  last-writer-wins with manual blank clears — observed mechanics, not
  contract; ADR-0024 and #283 replace them.

## #92 stale-result matrix

The named matrix #92 turns into executable tests. "Stale" means the operation
must not install a session and must not write SolidWorks Document Properties —
regardless of when the underlying request resolves.

| # | Scenario | Expected outcome |
| --- | --- | --- |
| 1 | Fetch in flight; the active document changes before it completes | Stale — no session install, no writes to the newly active document |
| 2 | Fetch in flight; the document closes before it completes | Stale — no session install, no writes |
| 3 | Fetch in flight; the InvenTree client is replaced (`UpdateClient`) | Stale — coordinator lifecycle revision advanced |
| 4 | Fetch in flight; the Property Mapping is replaced (`UpdateMapping`) | Stale — coordinator lifecycle revision advanced |
| 5 | Fetch in flight; a newer Fetch is issued in the same generation | The older completion is stale (request-order component) |
| 6 | Create Part completion after a document switch or close | Stale — no session install, no writes |
| 7 | Create Part completion after client replacement or Property Mapping replacement | Stale |
| 8 | Completed result; then a switch to a document stamped with the same IPN and InvenTree Part PK | Revalidated and adopted for the new generation |
| 9 | Completed result; then a switch to a document with different identity stamps | Dropped — the new document evaluates on its own stamps |

Keep the matrix distinct from the completed-session rules: rows 1–7 cover
work still in flight, rows 8–9 cover a completed result revalidated on a
document switch. The five completed-session cases pinned in
`TaskPaneLifecycleCharacterizationTests` describe what today's
`LoadPartNumber` / `OnDocumentPropertyChanged` do once a session exists.
