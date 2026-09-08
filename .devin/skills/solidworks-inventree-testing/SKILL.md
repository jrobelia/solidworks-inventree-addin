---
name: solidworks-inventree-testing
description: Test the SolidWorks InvenTree Add-In WPF UI when SolidWorks itself is not installed, by hosting the add-in's windows/viewmodels in a temporary WPF harness.
---

# Testing the SolidWorks InvenTree Add-In UI without SolidWorks installed

SolidWorks is not always available in CI or test environments, but most of the add-in's WPF dialogs and viewmodels can be exercised directly because the `SwInventreeAddin` assembly targets `net48` with WPF and the interop references are only used at runtime by the SolidWorks COM host.

## When to use this skill

- The build environment has .NET Framework 4.8 targeting pack and `dotnet` CLI but no SolidWorks installation.
- You need to verify a dialog (`CreatePartWindow`, `BomCompareWindow`, `SettingsWindow`, etc.) visually.
- The automated unit tests pass but you still need runtime proof of the UI state, bindings, or tooltips.

## Prerequisites

- `dotnet` SDK (the repo blueprint installs `dotnet-8.0-sdk` and `netfx-4.8-devpack`).
- The repo has already restored the BlueByte SOLIDWORKS interop stand-ins via `Directory.Build.props.user`.
- The `SwInventreeAddin` project builds cleanly.

## Running the automated suite

```powershell
dotnet build "SwInventreeAddin/SwInventreeAddin.csproj" --disable-build-servers
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers
```

## Creating a temporary WPF harness for a dialog

1. Create a new temporary .NET Framework 4.8 WPF executable outside the repo, e.g. `C:\Temp\AddinDialogHarness`.
2. Add a `ProjectReference` to `SwInventreeAddin/SwInventreeAddin.csproj` (not the compiled DLL) so transitive dependencies flow.
3. Implement minimal stubs of `IInventreeClient` and `IDocumentPropertyService` (and `IPropertyMappingProvider` / `IViewportCaptureService` if needed). Return empty lists/safe defaults so the viewmodel can initialize.
4. In `Program.Main`, mark the thread `[STAThread]`, create the WPF `Application` or use `Window.ShowDialog()`, construct the viewmodel with the desired constructor arguments, call the window's `Initialise` method (for `CreatePartWindow`), and show it.
5. Use the desktop automation tooling to click, type, hover, and capture screenshots.

### Minimal stub example

```csharp
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;

class DummyClient : IInventreeClient
{
    public Task<IReadOnlyList<InventreeCategory>> GetCategoriesAsync(int? parentId)
        => Task.FromResult<IReadOnlyList<InventreeCategory>>(new List<InventreeCategory>());

    public Task<int> CreatePartAsync(int categoryPk, string name, string? ipn = null, PartCreationFlags? flags = null)
        => Task.FromResult(1);

    public Task<InventreePart?> GetPartByPkAsync(int pk) => Task.FromResult<InventreePart?>(null);
    public Task<InventreePart?> GetPartByIpnAsync(string ipn) => Task.FromResult<InventreePart?>(null);
    public Task<IReadOnlyList<InventreePart>> GetPartsByIpnAsync(string ipn)
        => Task.FromResult<IReadOnlyList<InventreePart>>(new List<InventreePart>());

    public Task UpdatePartNameAsync(int pk, string name) => Task.CompletedTask;
    public Task UpdatePartNotesAsync(int pk, string notes) => Task.CompletedTask;
    public Task UpdatePartDescriptionAsync(int pk, string description) => Task.CompletedTask;
    public Task UpdatePartRevisionAsync(int pk, string revision) => Task.CompletedTask;
    public Task UploadPartImageAsync(int pk, byte[] pngData) => Task.CompletedTask;
    public Task<byte[]?> DownloadImageAsync(string url) => Task.FromResult<byte[]?>(null);
    public Task<InventreeServerInfo> GetServerInfoAsync() => Task.FromResult(new InventreeServerInfo());
    public Task<IReadOnlyList<InventreeBomLine>> GetBomAsync(int assemblyPk)
        => Task.FromResult<IReadOnlyList<InventreeBomLine>>(new List<InventreeBomLine>());
    public Task<int> CreateBomLineAsync(int assemblyPk, int subPartPk, decimal quantity,
        string reference, string note, bool consumable, bool optional) => Task.FromResult(1);
    public Task UpdateBomLineAsync(int bomLinePk, decimal quantity,
        string reference, string note, bool consumable, bool optional) => Task.CompletedTask;
    public Uri? GetPartWebUrl(int pk) => null;
}

class DummyPropertyService : IDocumentPropertyService
{
    public DocumentType GetDocumentType() => DocumentType.Part;
    public string GetCustomProperty(string name) => string.Empty;
    public void SetCustomProperty(string name, string value) { }
    public bool PropertyExists(string name) => false;
}
```

## Opening the Create Part dialog

```csharp
[STAThread]
static void Main()
{
    var window = new CreatePartWindow();
    var client = new DummyClient();
    var props = new DummyPropertyService();
    var vm = new CreatePartViewModel(
        client, props, "Test Part",
        waitForServerAssignedIpn: true,
        documentType: DocumentType.Part);
    window.Initialise(vm);
    window.ShowDialog();
}
```

## Hosting the Task Pane for Apply-button testing

`TaskPaneView` is a `UserControl`, so it can be hosted inside a temporary WPF window.
This is useful for verifying `TaskPaneViewModel` states such as `CurrentName`,
`CurrentDescription`, `CurrentNotes`, `CurrentPk`, and their `*Match` indicators.

1. Create a temporary WPF project as above.
2. Add a `Window` containing the `TaskPaneView` control.
3. Implement `DummyClient` and a richer `DummyPropertyService`:

```csharp
class DummyPropertyService : IDocumentPropertyService
{
    private readonly Dictionary<string, string> _properties = new();

    public DocumentType DocumentTypeToReturn { get; set; } = DocumentType.Assembly;
    public bool ReturnStaleReads { get; set; }
    public string StaleValue { get; set; } = string.Empty;

    public DocumentType GetDocumentType() => DocumentTypeToReturn;
    public void Seed(string name, string value) => _properties[name] = value;

    public string GetCustomProperty(string name) =>
        ReturnStaleReads ? StaleValue
                       : _properties.TryGetValue(name, out var v) ? v : string.Empty;

    public void SetCustomProperty(string name, string value) => _properties[name] = value;
    public bool PropertyExists(string name) => _properties.ContainsKey(name);
}
```

4. Seed stale SW values, set `ReturnStaleReads = true`, fetch a part, then click each
   `Apply to SW Doc` command. The `DummyPropertyService` will keep returning the
   configured `StaleValue` on every read-back, simulating the cached-assembly bug.
5. After applying all four fields, turn `ReturnStaleReads` off and click
   `Load Properties from InvenTree` to prove `RefreshCurrentProperties` still works.

### Capturing the full TaskPaneView

Because `TaskPaneView` is inside a `ScrollViewer`, a live window will clip rows.
For deterministic screenshots, host the `TaskPaneView` in a temporary off-screen
`Window` with `SizeToContent = SizeToContent.WidthAndHeight`, measure/arrange it
with infinite size, and render it with `RenderTargetBitmap`. This captures the
whole control at its desired height.

```csharp
var taskPane = new TaskPaneView { DataContext = viewModel };
var captureWindow = new Window
{
    Content = taskPane,
    SizeToContent = SizeToContent.WidthAndHeight,
    WindowStyle = WindowStyle.None,
    AllowsTransparency = true,
    ShowInTaskbar = false,
    Left = -20000,
    Top = -20000,
    Opacity = 0.01
};
captureWindow.Show();
captureWindow.UpdateLayout();
taskPane.UpdateLayout();

taskPane.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
var width = Math.Max((int)taskPane.DesiredSize.Width, 440);
var height = Math.Max((int)taskPane.DesiredSize.Height, 600);
taskPane.Arrange(new Rect(0, 0, width, height));

var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
bmp.Render(taskPane);
```

## What this proves and what it does not

- Proves: XAML layout, control bindings, placeholder/tooltip text, enabled/disabled state, and viewmodel logic all work as intended.
- Does not prove: SolidWorks COM add-in registration, task-pane hosting, document property mapping, or the live `Create InvenTree P/N` button path. Those require a real SolidWorks session.

## Common issues

- `Window.Show()` followed by clicking `Cancel` throws because `CreatePartWindow.Cancel_Click` sets `DialogResult`. Use `Window.ShowDialog()` or avoid the Cancel button in non-modal harnesses.
- If a window black-screens or fails to render, make sure the harness project sets `<UseWpf>true</UseWpf>` and `<OutputType>Exe</OutputType>` and is running on an STA thread.
- Stub methods that touch `IInventreeClient` categories are often called during `Initialise`, so return empty lists to avoid null exceptions.

## Devin Secrets Needed

None for the standalone harness. Real end-to-end testing inside SolidWorks may need an InvenTree server URL and API key stored in the normal `InvenTree` config provider.
