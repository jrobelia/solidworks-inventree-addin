# AddIn Logger (IAddinLogger)

The add-in has no persistent diagnostics. Three `System.Diagnostics.Trace.WriteLine` calls in `SwAddin` and `SwAssemblyBomService` are lost because no listener is attached in the SolidWorks process. To debug a failed Fetch, Push, or BOM Compare in the field, maintainers need a file log they can ask an engineer to send.

## Decision

- Add a cross-cutting diagnostic seam: `IAddinLogger` with `LogInformation(string message)` and `LogError(string message, Exception? exception = null)`.
- `IAddinLogger` extends `IDisposable` and implementations are thread-safe.
- `SwAddin` creates one `IAddinLogger` in `ConnectToSW` and passes it to `TaskPaneControl`, `TaskPaneViewModel`, and `BomCompareViewModel`; `SwAddin` disposes it in `DisconnectFromSW`.
- Production adapter: `FileAddinLogger` in `SwInventreeAddin.Logging`, writing to `%LOCALAPPDATA%\SwInventreeAddin\logs\inventree-addin.log`.
- Size cap: rollover at 1 MB, keeping 3 files total; the oldest file is overwritten.
- `FileAddinLogger` accepts an optional `logDirectory` so tests can use a temp path instead of `%LOCALAPPDATA%`.
- Redaction: `FileAddinLogger` is constructed with a list of secret strings (e.g. the InvenTree API key). Before writing, it replaces exact and substring matches with `***`. Callers must still never intentionally log secrets.
- Info-level log messages must not include COM or interop internals; error-level messages may include exception text.
- Initial instrumentation: `SwAddin` startup, teardown, and unhandled errors; entry points of Part Sync (`FetchPartAsync`, all Apply/Push commands, `PushImageAsync`) and BOM Compare (`BomCompareViewModel.LoadAsync` and `PushAsync`).
- Test adapter: `StubAddinLogger` in `SwInventreeAddin.Tests/Stubs/` capturing a list of `(Level, Message, Exception?)` entries.
- The log path is documented in this ADR and the issue; it is not surfaced in the Settings window for this pass.
- The redaction rule will be folded into `docs/agents/coding-standards.md` by #182; until then it lives here.

## Consequences

- The three existing `Trace.WriteLine` calls are replaced with `IAddinLogger` calls.
- New constructors for `TaskPaneControl`, `TaskPaneViewModel`, and `BomCompareViewModel` accept `IAddinLogger`.
- Tests run with `StubAddinLogger` or a `NullAddinLogger` default; file-logger tests use a temp directory.
- The interface is intentionally small; per-call service instrumentation is out of scope.
