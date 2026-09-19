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
