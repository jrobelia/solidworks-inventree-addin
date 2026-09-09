# Coding Standards

This file defines the coding standards for this project. It is the Standards-axis rulebook for `/review` — run by `/build`, `/fix`, and `/build-afk`'s review step — and the source of the agent verification command and module-design rules those skills apply.

---

## Language & Framework

Language: C# 8.0
Runtime: .NET Framework 4.8 (`net48`)
UI: WPF (XAML + code-behind)
InvenTree client: `SwInventreeAddin/InvenTree/` — wraps InvenTree REST endpoints; uses `System.Text.Json`

Style guide: Microsoft C# coding conventions. Nullable reference types enabled. No C# 9+ features (`init`, `record`, top-level statements).

---

## Build & Test Commands

Agent verification: `dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers`. Agents run this command only — it compiles the add-in into `bin_unit_test\net48` and runs the suite while SolidWorks is open.

The `dotnet build` commands below produce the SolidWorks-facing `bin\Debug\net48` output. SolidWorks locks `bin\Debug\net48\SwInventreeAddin.dll` while running, so they fail on the file copy and are manual steps only — run them with SolidWorks closed when the add-in DLL itself needs refreshing.

Build: `dotnet build "SwInventreeAddin/SwInventreeAddin.csproj" --disable-build-servers` — builds just the add-in project.
Solution build: `dotnet build "Solidworks Inventree Add-In.sln" --disable-build-servers` — builds the full solution, including the test project.

Check: `dotnet format "Solidworks Inventree Add-In.sln" --verify-no-changes` — enforces the root `.editorconfig`; CI runs it on every push. `Microsoft.CodeAnalysis.NetAnalyzers` warnings surface inside `dotnet test` / `dotnet build`.

Notes:
- All commands above use `--disable-build-servers` and `UseSharedCompilation=false` in `Directory.Build.props` to stop long-running `dotnet` and `VBCSCompiler` processes from holding file locks.

---

## Naming Conventions

- Classes and interfaces: `PascalCase` (`TaskPaneViewModel`, `IInventreeClient`)
- Interfaces: prefixed with `I` (`IInventreeClient`, `IDocumentPropertyService`)
- Methods: `PascalCase` (`GetPartByIpnAsync`, `LoadRootCategoriesAsync`)
- Private fields: `_camelCase` (`_client`, `_isBusy`, `_propertyService`)
- Local variables: `camelCase` (`partNumber`, `selectedCategory`)
- Properties: `PascalCase` (`PartNumber`, `CreateEnabled`)
- Test methods: `MethodOrState_Scenario_ExpectedResult` (`CreateEnabled_NoCategory_IsFalse`)
- Async methods: suffix with `Async` (`FetchPartAsync`, `UpdatePartRevisionAsync`)
- Stub classes: prefix with `Stub` (`StubInventreeClient`, `StubDocumentPropertyService`)

---

## Test Conventions

Framework: NUnit 3.14

Structure: Arrange / Act / Assert (implicit). Use `[TestFixture]`, `[SetUp]`, `[Test]`, `[TestCase]`. Async tests use `async Task`.

Location: All tests in `SwInventreeAddin.Tests/`. Stubs live in `SwInventreeAddin.Tests/Stubs/`. One test file per production class.

Rules:
- Unit tests must run on a machine with no SolidWorks installed. Every host-facing dependency sits behind a `Stub*` adapter so the suite never loads SolidWorks interop assemblies.
- Tests use stub implementations (from `Stubs/`), never mocking frameworks.
- Each test verifies one logical assertion using the NUnit constraint model: `Assert.That(x, Is.EqualTo(y))` — never the classic `Assert.AreEqual`.
- Tests must not depend on each other or rely on execution order.
- ViewModels must be constructable in tests without STA threads, WinForms, or WPF controls.
- Live-window tests that `Show()` a real window must keep it off every monitor via `HiddenTestWindow`. Park the owner with `CreateOwnerForm()` and assert the dialog rect stays off-screen with `IsOnScreen`. Never hide with `Opacity` (it stops rendering and leaves an unpainted black window) or with a maximized on-screen owner (it snaps back onto a monitor).
- Use `[SetUp]` to construct stubs; use a private factory method (e.g. `CreateVm(...)`) to construct the subject under test.

---

## Module Design

Design **deep modules**: a lot of behaviour behind a small interface, placed at a clean **seam**, testable through that interface. This gives callers **leverage**, maintainers **locality**, and the codebase **testability**.

### Vocabulary

- **Module** — the umbrella term for anything with an interface and an implementation: a function, class, package, or cross-layer slice.
- **Interface** — everything a caller must know to use the module correctly: signatures, invariants, ordering constraints, error modes, configuration, and performance.
- **Depth** — behaviour per unit of interface. A module is **deep** when a large amount of behaviour sits behind a small interface, **shallow** when the interface is nearly as complex as the implementation.
- **Seam** — the place where a module's interface lives; where behaviour can change without editing the caller.
- **Adapter** — a concrete implementation at a seam. `InventreeHttpClient` and `StubInventreeClient` both satisfy `IInventreeClient`; `SwDocumentPropertyService` and `StubDocumentPropertyService` both satisfy `IDocumentPropertyService`; `SwAssemblyBomService` and `StubAssemblyBomService` both satisfy `IAssemblyBomService`; `SwViewportCaptureService` and `StubViewportCaptureService` both satisfy `IViewportCaptureService`.
- **Leverage** — more capability per unit of interface learned.
- **Locality** — bugs, knowledge, and verification concentrated in one place.

### Design tests

- **The deletion test.** Deleting the module should force its complexity back onto callers; if it only passes through, it is shallow.
- **The interface is the test surface.** Callers and tests cross the same seam.
- **One adapter means a hypothetical seam. Two adapters means a real one.** Introduce an interface only when a production adapter and a test adapter both sit at the seam.
- **Depth is a property of the interface, not the implementation.** A module can be composed internally; the caller must not see the parts.
- **YAGNI and simplicity.** Only deepen a module or introduce a seam where the code has already shown a need: multiple callers, real test variation, or repeated change. Prefer a boring, direct implementation until the simple version leaks complexity or forces duplication.

### Applying it here

The rules in this file are consequences of this philosophy:

- **Interfaces for all cross-layer dependencies** create real seams. `IInventreeClient`, `IDocumentPropertyService`, `IAssemblyBomService`, and `IViewportCaptureService` each have production and `Stub*` test adapters.
- **No static state** keeps module interfaces explicit.
- **No business logic in UI code** puts depth behind services and ViewModels, not in XAML code-behind. A `Part Sync` module exposes `Fetch`, `Apply`, and `Push`; a `BOM Compare` module hides SolidWorks BOM table traversal and InvenTree diff logic behind `Compare` and `Push`.
- **Accept dependencies, don't create them.** Prefer `public SomeService(IInventreeClient client)` over constructing `new InventreeHttpClient()` inside the class.

Before adding a public method or class, ask:

1. Can I reduce the number of methods?
2. Can I simplify the parameters?
3. Can I hide more complexity inside?

For the shared vocabulary, design-it-twice patterns, and deepening guidance, consult the `/codebase-design` skill. This `## Module Design` section remains the repo's local source of truth; do not duplicate those definitions elsewhere.

---

## Code Quality Rules

- **No business logic in UI code.** ViewModels call services; services own logic. XAML code-behind only wires events and delegates to the ViewModel.
- **No static state.** Pass all dependencies through constructors.
- **Interfaces for all cross-layer dependencies.** Every external service (InvenTree client, document property service, viewport capture) must be accessed through an interface so it can be stubbed in tests.
- **`System.Text.Json` only.** Never use Newtonsoft.Json.
- **SolidWorks DLLs never copied to output.** `Private=False`, `EmbedInteropTypes=True` — no exceptions.
- **Release field-held SolidWorks COM references in `DisconnectFromSW`.** Interop objects stored in fields (`_swApp`, `_taskPaneView`) are released with `Marshal.ReleaseComObject` and nulled during teardown. Short-lived per-call COM references do not require explicit release.
- **Property names user-configurable.** Never hardcode SolidWorks ↔ InvenTree property name mappings; always read from `IPropertyMappingProvider`.
- **IPN is server-side.** After creating a part, always re-fetch from InvenTree to get the assigned IPN. Never assume or generate the IPN locally.
- **InvenTree-only BOM lines are read-only.** The add-in must never modify or delete them.
- **No comments describing what the code does.** Only explain non-obvious *why* decisions. XML doc comments on public APIs are welcome.
- **`ConfigureAwait(false)` on all HTTP awaits; `RunOnUiThread` for UI updates.** In ViewModels, await HTTP calls with `.ConfigureAwait(false)` so they run on the thread pool. Then wrap all property sets and status updates in `RunOnUiThread(...)` to marshal back to the STA thread. Do not use `ConfigureAwait(true)` as a substitute for `RunOnUiThread`. See ADR 0002.
- **`async void` only on event handlers.** Every other async method returns `Task`. An unhandled throw in `async void` has no caller to catch it and crashes the SolidWorks host process.
- **Catch at the event-handler boundary and surface errors in a dialog.** Event handlers catch exceptions and show `MessageDialog` owned by the SolidWorks window handle (`SolidWorksWindowHandle.Get()`) or the add-in window that spawned the action. An error that only reaches Task Pane status text can pass unnoticed.
- **`Set<T>` for all `INotifyPropertyChanged` properties.** Use the `Set(ref _field, value)` helper rather than calling `PropertyChanged` directly. Computed properties (no backing field) fire `PropertyChanged` explicitly from the setters of their dependencies.
- **Batch data-bound collection updates.** When updating a data-bound `ObservableCollection`, update items in place or raise a single `Reset` notification rather than calling `Clear()` followed by multiple `Add()` calls. Each `Clear`/`Add` raises a separate `CollectionChanged` event and triggers a WPF layout pass; during a host repaint callback (e.g. a SolidWorks view notification), re-entrant layout can crash the host process.
- **Section separator comments.** Use `// ── Section name ─────` dividers to separate logical sections within a class (Dependencies, Bindable properties, State, Constructors, Commands, Behaviour, Helpers). Match the existing style exactly.
- **No column-aligned declarations.** `dotnet format` enforces single-space layout — do not hand-align columns; the verify step in CI rejects it.
- **Domain terminology.** Use terms from `CONTEXT.md`: IPN (not part number), Fetch (not load/pull), Apply (InvenTree → SW), Push (SW → InvenTree), Task Pane (not sidebar/panel). Use these in identifiers, comments, and status strings.

---

## What Reviewers Look For

- Business logic or InvenTree API calls placed directly in XAML code-behind instead of the ViewModel.
- Missing interface for a new dependency (makes it untestable).
- Tests that use `Assert.AreEqual` or `Assert.IsTrue` instead of the constraint model.
- Tests that test multiple independent behaviours in a single `[Test]` method.
- Hardcoded property name strings that should come from `IPropertyMappingProvider`.
- IPN assumed after creation instead of re-fetched.
- `ThrowOnUpdate` / `ThrowOnUpload` / exception paths not covered in tests when the new code can throw.
- Forgetting to dispose `IDisposable` resources (e.g. `HttpClient`, `Bitmap`).
- A SolidWorks COM reference stored in a field that `DisconnectFromSW` does not release.
- Missing `RunOnUiThread` wrapper around property sets inside an async method — silently breaks on the STA thread.
- Using `ConfigureAwait(true)` or omitting `ConfigureAwait` on HTTP awaits in ViewModels.
- `async void` on a method that is not an event handler — an unhandled throw crashes the SolidWorks host process.
- An event handler that lets an exception escape or reports failure only to Task Pane status text, instead of showing a `MessageDialog` owned by the SolidWorks window.
- New properties using `PropertyChanged?.Invoke(...)` directly instead of the `Set<T>` helper.
- Data-bound `ObservableCollection` updated with `Clear()` + multiple `Add()` instead of in-place updates or a single `Reset` — risks re-entrant WPF layout crashes during host repaint callbacks.
- Domain terminology violations: `Load` instead of `Fetch`, `sync` instead of `Apply`/`Push`, `part number` instead of `IPN`.
- Shallow modules where the interface is nearly as complex as the implementation (pass-throughs, thin wrappers).
- Missing locality: business logic or state duplicated across callers instead of living in a deep module.
- Hypothetical seams: a new cross-layer dependency with an interface but no `Stub*` test adapter.
- Tests that bypass the seam and exercise internal helpers rather than the module's public interface.
- Test code that references SolidWorks interop types or otherwise cannot run on a machine without SolidWorks installed.
- Live-window tests missing the `HiddenTestWindow` off-screen guard.
- New modules or seams introduced before the code shows a real need for them (YAGNI / over-engineering).
