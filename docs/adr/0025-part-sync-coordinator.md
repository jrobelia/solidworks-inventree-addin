# Part Sync Coordinator — Session Ownership, Operation Tokens, and the STA Commit Discipline

## Context

Until #92, `TaskPaneViewModel` owned the Part Sync session directly: the
`PartSyncSession` field, the `_pendingDocumentWrites` echo set, document
capture/install, Fetch/Apply/Push orchestration, Create Part stamping, and the
BOM readiness mixture (`IBomReadinessSource`). Async completions committed
through the VM's `RunOnUiThread` with only a document-generation check — a
completion could not distinguish "same document, newer operation" from "same
operation", and a Create Part dialog answer or a network continuation could
land on state that had moved on (document switch, client replacement, mapping
replacement, shutdown). `TaskPaneState` risked becoming the workflow god
object the extraction was meant to prevent.

Issue #92 (under parent spec #89) mandated a deep workflow module that owns
session lifecycle and orchestration while `TaskPaneState` stays state-only
and the ViewModel shrinks to a projection adapter.

## Decision

**`PartSyncCoordinator`** is the single owner of the Part Sync session and
its lifecycle, behind the narrow `IPartSyncCoordinator` interface.

- **Operation tokens.** Every async operation captures an opaque
  `PartSyncOperationToken` {document generation, lifecycle revision,
  session-family order} on the host STA thread. Document transitions advance
  the generation; `UpdateClient`, `UpdateMapping`, and `Dispose` advance the
  revision; `FetchAsync` and `BeginCreatePart` mint a new family order —
  superseding earlier family operations while sequential scoped operations
  (Push, image upload, confirmations) never stale each other.
- **Commit discipline.** STA capture → network/image work off-thread via
  `ConfigureAwait(false)` → commit marshalled through `IHostStaDispatcher`
  with token revalidation **inside** the marshalled callback. One guard —
  `IsCommitCurrent` = token validation + active-document recapture (which
  catches a switch whose host notification has not arrived) — runs BEFORE
  any result examination or write on every commit path, including error
  branches: a stale `Failed` can never reach the ViewModel as a status
  write, and `CompleteCreatePart`/confirmation resumes revalidate before
  touching `ISldWorks.ActiveDoc`. No stale completion ever installs a
  session, mutates a dropped session, or writes a Document Property.
- **Typed outcomes, not UI.** Operations return `PartSyncResult` carrying a
  `PartSyncOutcome` (Success, PartNotFound, DuplicateIpnConfirmation,
  DuplicateNoRevisionMatch, DuplicateAmbiguous, LinkMismatchConfirmation,
  MissingPropertyConfirmation, Stale, Cancelled, InvalidOperation, Failed,
  SucceededWithWarning). The ViewModel owns all status wording and prompts;
  coordinator code never touches `StatusText`.
- **Confirmation correlation.** Every confirmation outcome carries an opaque
  `PartSyncConfirmationHandle`; `ResumeConfirmationAsync` validates it
  against the single pending-confirmation slot, validates the operation
  token on resume (and again inside the post-download commit), so an
  approval can never land on a different pending operation than the one
  displayed — and a stale resume is rejected before any download starts.
  Duplicate candidate selection is by immutable PK, validated against the
  captured set, which is a `ReadOnlyCollection` the caller cannot mutate.
- **Immutable surface.** `InventreePart` never escapes: the read surface and
  result payloads expose immutable `PartSnapshot` projections, incoming parts
  are copied on install, and `ThumbnailBytes`/`CurrentMapping` return
  defensive copies. `PartSyncSession` is `internal` — only the coordinator
  creates, replaces, or clears it.
- **Pending-write echoes** live in one coordinator-owned dictionary shared
  with the installed session; every coordinator-originated write registers
  before `SetCustomProperty` (SolidWorks can raise the echo synchronously)
  and the set is cleared only on generation advance.
- **BOM readiness** consumes `IBomReadinessContext` — one coherent
  `BomReadinessSnapshot` (immutable; clones its mapping in and out) plus
  the explicit ensure-populated and push-revision commands — replacing
  `IBomReadinessSource`'s property-and-command mixture. Non-success,
  non-confirmation ensure outcomes surface as `BomCompareOutcome.FetchFailed`
  carrying the typed `PartSyncResult` — a server or lifecycle failure never
  masquerades as "create the part". The production adapter marshals snapshot
  captures through the dispatcher, so readiness checks never touch
  SolidWorks COM on a pool continuation.
- **`IHostStaDispatcher`** is the marshalling seam:
  `SynchronizationContextStaDispatcher` (production, captures the host STA
  context in `TaskPaneControl`; managed-thread-id check per ADR-0002) and
  `StubHostStaDispatcher` (tests, with a queued-commit mode that parks
  commits so tests can land a document switch mid-flight).

## Considered options

- **Move workflow onto `TaskPaneState`** — rejected: the spec's explicit
  constraint. State stays a document/generation holder; the coordinator owns
  it.
- **Keep `IBomReadinessSource`** — rejected: it mixed queries, Fetch, refresh,
  and Push Revision, and read SolidWorks from post-await continuations.
- **CancellationToken-based staleness** — rejected: `IInventreeClient` carries
  no tokens; token validation is the correctness mechanism, cancellation is
  best-effort only.
- **Mutable `InventreePart` on the read surface** — rejected at Gate 1:
  consumers could mutate session state behind the coordinator's back.

## Consequences

- `TaskPaneViewModel` projects `IPartSyncCoordinator` into bindable state and
  maps outcomes to status/prompts; it owns no session, no pending writes, and
  no direct document-property writes. `CreatePartViewModel` likewise returns
  the created part via `PartCreated` — stamping is
  `CompleteCreatePart`'s stale-validated commit.
- `PartThumbnailService` is deleted — upload + thumbnail re-fetch moved into
  the coordinator's `PushImageAsync`; its warning messages travel as
  `SucceededWithWarning` diagnostics.
- The light document refresh (capture→install without same-document session
  revalidation) is an `internal` member on the concrete
  `PartSyncCoordinator` — `IPartSyncCoordinator` is exactly the approved
  public seam; only the internal BOM adapter and the ViewModel (both bound
  to the concrete type) consume it.
- Every async mutation is now provably generation/lifecycle/order-guarded:
  overlapping fetches, fetch-vs-create races, client/mapping replacement, and
  disposal each have executable stale-commit tests
  (`PartSyncCoordinatorTests`), including the queued-dispatcher commit-park
  scenario.
- `TaskPaneState` gained only the `PopulatedGeneration` signal
  (`MarkPopulated`/`ClearPopulated`) — a generation-bound marker, not
  workflow.
