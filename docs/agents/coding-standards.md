# Coding Standards

This file defines the coding standards for this project. It is used by RALPH's review agent to evaluate code quality after each implementation.

---

## Language & Framework

Language: C# 8.0
Runtime: .NET Framework 4.8 (`net48`)
UI: WPF (XAML + code-behind)
InvenTree client: `SwInventreeAddin/InvenTree/` — wraps InvenTree REST endpoints; uses `System.Text.Json`

Style guide: Microsoft C# coding conventions. Nullable reference types enabled. No C# 9+ features (`init`, `record`, top-level statements).

---

## Build & Test Commands

Build: `dotnet build "Solidworks Inventree Add-In.sln"`
Test: `dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj"`

Check: (none — no separate lint step)

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
- Tests use stub implementations (from `Stubs/`), never mocking frameworks.
- Each test verifies one logical assertion using the NUnit constraint model: `Assert.That(x, Is.EqualTo(y))` — never the classic `Assert.AreEqual`.
- Tests must not depend on each other or rely on execution order.
- ViewModels must be constructable in tests without STA threads, WinForms, or WPF controls.
- Use `[SetUp]` to construct stubs; use a private factory method (e.g. `CreateVm(...)`) to construct the subject under test.

---

## Code Quality Rules

- **No business logic in UI code.** ViewModels call services; services own logic. XAML code-behind only wires events and delegates to the ViewModel.
- **No static state.** Pass all dependencies through constructors.
- **Interfaces for all cross-layer dependencies.** Every external service (InvenTree client, document property service, viewport capture) must be accessed through an interface so it can be stubbed in tests.
- **`System.Text.Json` only.** Never use Newtonsoft.Json.
- **SolidWorks DLLs never copied to output.** `Private=False`, `EmbedInteropTypes=True` — no exceptions.
- **Property names user-configurable.** Never hardcode SolidWorks ↔ InvenTree property name mappings; always read from `IPropertyMappingProvider`.
- **IPN is server-side.** After creating a part, always re-fetch from InvenTree to get the assigned IPN. Never assume or generate the IPN locally.
- **InvenTree-only BOM lines are read-only.** The add-in must never modify or delete them.
- **No comments describing what the code does.** Only explain non-obvious *why* decisions. XML doc comments on public APIs are welcome.
- **`ConfigureAwait(false)` on all HTTP awaits; `RunOnUiThread` for UI updates.** In ViewModels, await HTTP calls with `.ConfigureAwait(false)` so they run on the thread pool. Then wrap all property sets and status updates in `RunOnUiThread(...)` to marshal back to the STA thread. Do not use `ConfigureAwait(true)` as a substitute for `RunOnUiThread`. See ADR 0002.
- **`Set<T>` for all `INotifyPropertyChanged` properties.** Use the `Set(ref _field, value)` helper rather than calling `PropertyChanged` directly. Computed properties (no backing field) fire `PropertyChanged` explicitly from the setters of their dependencies.
- **Section separator comments.** Use `// ── Section name ─────` dividers to separate logical sections within a class (Dependencies, Bindable properties, State, Constructors, Commands, Behaviour, Helpers). Match the existing style exactly.
- **Column-aligned field declarations.** Private field blocks align types and names vertically with spaces (not tabs). Match the surrounding alignment when adding new fields.
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
- Missing `RunOnUiThread` wrapper around property sets inside an async method — silently breaks on the STA thread.
- Using `ConfigureAwait(true)` or omitting `ConfigureAwait` on HTTP awaits in ViewModels.
- New properties using `PropertyChanged?.Invoke(...)` directly instead of the `Set<T>` helper.
- Domain terminology violations: `Load` instead of `Fetch`, `sync` instead of `Apply`/`Push`, `part number` instead of `IPN`.
