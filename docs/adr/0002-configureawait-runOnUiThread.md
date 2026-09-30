# ConfigureAwait(false) + RunOnUiThread for STA thread safety

SolidWorks runs on an STA thread. Task.Run forced continuations onto thread pool threads, which deadlocked NUnit's STA runner and risked COM violations. Direct await with ConfigureAwait(false) lets the HTTP call run on the thread pool naturally, then RunOnUiThread marshals back via Invoke.

## Addendum — the mechanism is pinned (#264)

`RunOnUiThread` captures `SynchronizationContext.Current` and
`Environment.CurrentManagedThreadId` at construction. A caller on the captured
thread runs inline; a caller on any other thread marshals through `Send`
(synchronous — the caller sees the update applied before it continues); a null
captured context runs inline (unit tests). `SettingsViewModel` carries the
canonical implementation and its regression tests.

The thread-id check is load-bearing: inside `Dispatcher.Invoke`,
`SynchronizationContext.Current` is a fresh `DispatcherSynchronizationContext`
wrapper that never reference-equals the captured instance. A context-comparing
implementation therefore `Send`s into a context whose pump is not running — a
hang for `Send`, silent work-loss for `Post`. The three older copies
(`TaskPaneViewModel`, `BomCompareViewModel`, `CreatePartViewModel`) predate
this pin and are aligned under #264.

The complete Task Pane lifecycle contract — STA capture → off-thread network
work → STA validation/commit, plus the operation-token stale-result rules —
is documented in `docs/agents/task-pane-lifecycle.md`. This ADR pins the
marshalling mechanism; that document pins when a completion may commit at all.

## Addendum — who may touch which thread (#93)

- **Capture** runs on the host STA thread only. `TaskPaneControl` routes every
  SolidWorks callback straight into the coordinator's lifecycle members
  (`UpdateDocument`, `NotifyDocumentClosed`, `NotifyDocumentPropertyChanged`,
  `UpdateClient`, `UpdateMapping`), which read Document Properties and mint
  the operation token inside the same STA call. The ViewModel reads no
  Document Properties at all — it projects the coordinator's immutable
  snapshot surface.
- **Network work** runs off-thread under `ConfigureAwait(false)`.
- **Validation and commit** run back on the STA thread inside
  `IHostStaDispatcher.Run`: token revalidation plus an active-document
  recapture, then the write/session install. A completion whose captured
  generation or lifecycle revision no longer matches is `Stale` — it writes
  nothing and surfaces no result, so a late network answer can never put a
  status on a pane that belongs to a newer document.
- **Notification** re-marshals through the same dispatcher: the coordinator's
  `Changed` event reaches the ViewModel's property notifications on the
  captured thread via the managed-thread-id rule above.
