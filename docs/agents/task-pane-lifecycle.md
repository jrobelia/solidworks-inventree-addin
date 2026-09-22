# Task Pane lifecycle

The Task Pane's state and lifecycle contract, locked down in #90 ahead of the
#91–#93 state moves. The five kinds below are characterized as observable
`TaskPaneViewModel` state by `TaskPaneLifecycleCharacterizationTests`
(`SwInventreeAddin.Tests/TaskPaneViewModelTests.cs`); the `#92` matrix is
executable — `PartSyncCoordinatorTests` pins every row against the real
coordinator.

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
- **Operation token.** `PartSyncCoordinator` captures an opaque
  `PartSyncOperationToken` — document generation + coordinator lifecycle
  revision + session-family order — at STA capture time. The lifecycle
  revision advances on client replacement (`UpdateClient`), Property Mapping
  replacement (`UpdateMapping`), and disposal/shutdown. Fetch and
  `BeginCreatePart` mint a new family order, so overlapping family operations
  stale each other while sequential scoped operations (Push, image,
  confirmations) do not.
- **Stale completion.** A completion whose captured token no longer matches
  must not install a session, must not write SolidWorks Document
  Properties, and must not surface *any* result — including a network
  failure — to the pane. Every commit path (marshalled or synchronous)
  runs one guard first: token revalidation *inside* the marshalled commit
  plus an active-document recapture that catches a switch whose host
  notification has not arrived yet. A stale `Failed` returns `Stale`, so it
  can never overwrite a newer document's status.
- **Cancellation is best effort.** `IInventreeClient` carries no
  `CancellationToken`s, so cancellation is *attempted* on document switch,
  close, client replacement, and shutdown — but token validation is the
  correctness mechanism, not cancellation.
- **Threading.** SolidWorks work follows STA capture → off-thread network
  work → STA validation/commit. `TaskPaneControl` constructs
  `SynchronizationContextStaDispatcher` on the host thread — it captures the
  ambient `SynchronizationContext` and the managed thread id; the
  coordinator's commits and the ViewModel's `RunOnUiThread` marshal through
  its `Run` — a managed-thread-id check, never a context comparison
  (ADR-0002). The rule is testable without SolidWorks installed:
  `StubHostStaDispatcher` parks commits in queued mode so a test can land a
  document switch mid-flight, and `StubSynchronizationContext` counts `Send`
  calls for the marshalling path.
- **Presentation.** Confirmations (Link Mismatch, duplicate IPN, missing
  properties) and status wording remain presentation concerns. Producer
  triggers and current wording are catalogued in
  [status-producer-map.md](status-producer-map.md). Today's strip is
  last-writer-wins with manual blank clears — observed mechanics, not
  contract; ADR-0024 and #283 replace them.

## #92 stale-result matrix

The matrix #92 pins as executable `PartSyncCoordinatorTests`. "Stale" means
the operation must not install a session and must not write SolidWorks
Document Properties — regardless of when the underlying request resolves.

| # | Scenario | Expected outcome |
| --- | --- | --- |
| 1 | Fetch in flight; the active document changes before it completes | Stale — no session install, no writes to the newly active document |
| 2 | Fetch in flight; the document closes before it completes | Stale — no session install, no writes |
| 3 | Fetch in flight; the InvenTree client is replaced (`UpdateClient`) | Stale — coordinator lifecycle revision advanced |
| 4 | Fetch in flight; the Property Mapping is replaced (`UpdateMapping`) | Stale — coordinator lifecycle revision advanced |
| 5 | Fetch in flight; a newer Fetch is issued in the same generation | The older completion is stale (request-order component) |
| 6 | Create Part completion after a document switch or close | Stale — no session install, no writes |
| 7 | Create Part completion after client replacement or Property Mapping replacement | Stale |
| 8 | Push/image upload *fails* while its commit is parked; the document switches before it runs | Stale — the failure never becomes a status write on the new pane |
| 9 | Missing-property approval or duplicate-IPN resume after a document switch (delivered or not) | Stale — no `Apply` write, no session install; the duplicate resume is rejected before its thumbnail download |
| 10 | Completed result; then a switch to another document — any stamp combination, including an identical IPN + InvenTree Part PK | Dropped — the new document evaluates on its own stamps |

Keep the matrix distinct from the completed-session rules: rows 1–9 cover
work still in flight, row 10 covers a completed result across a document
switch. Row 10 is executable since #91 — `DocumentSwitch_*` tests in
`SwInventreeAddin.Tests/TaskPaneViewModelTests.cs` pin the token-based,
unconditional drop (#292: same-stamp adoption was rejected — two documents
sharing an IPN + InvenTree Part PK are a copied file with stale stamps);
rows 1–9 are `PartSyncCoordinatorTests` — including the queued-dispatcher
case where a commit is parked on the STA queue while the document changes.
The five
completed-session cases pinned in
`TaskPaneLifecycleCharacterizationTests` describe what
`LoadPartNumber` / `OnDocumentPropertyChanged` do once a session exists.
