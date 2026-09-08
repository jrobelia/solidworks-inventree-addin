# WPF smoke-harness for build-afk

This file is copied next to `CHILD_PROMPT.md` for each `/build-afk` run. The child agent should follow these steps when the diff touches UI/XAML/ViewModel code.

## When to use

- The build environment has .NET Framework 4.8 targeting pack and `dotnet` CLI but no SolidWorks installation.
- The automated `dotnet test` suite passes but the change touches WPF windows, viewmodels, dialogs, or XAML.

## One-time harness setup

Create a temporary .NET Framework 4.8 WPF executable outside the main repo, e.g. `C:\devin\worktrees\build-issue-{issue}\wpf-smoke\`.

```powershell
New-Item -ItemType Directory -Path C:\devin\worktrees\build-issue-{issue}\wpf-smoke -Force
Set-Location C:\devin\worktrees\build-issue-{issue}\wpf-smoke
```

Create a new WPF project that targets `net48` and references the add-in project (not the compiled DLL) so transitive dependencies flow:

```xml
<!-- WpfSmoke.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <UseWpf>true</UseWpf>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\..\..\repos\solidworks-inventree-addin\SwInventreeAddin\SwInventreeAddin.csproj" />
  </ItemGroup>
</Project>
```

Adjust the relative path in `ProjectReference` so it resolves from `C:\devin\worktrees\build-issue-{issue}\wpf-smoke\` to the checked-out worktree.

## Minimal stubs

At minimum, stub the two interfaces the add-in abstracts behind:

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

If the affected viewmodel needs `IPropertyMappingProvider`, `IAssemblyBomService`, or `IViewportCaptureService`, add equally minimal stubs returning safe defaults.

## Hosting a window

Use `[STAThread]` and `Window.ShowDialog()` for modal dialogs, or host a `UserControl` such as `TaskPaneView` inside a plain `Window`:

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

For `TaskPaneView`:

```csharp
[STAThread]
static void Main()
{
    var taskPane = new TaskPaneView();
    var window = new Window
    {
        Content = taskPane,
        SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
        WindowStyle = WindowStyle.None,
        AllowsTransparency = true,
        ShowInTaskbar = false,
        Left = -20000,
        Top = -20000,
        Opacity = 0.01
    };

    var vm = new TaskPaneViewModel(new DummyClient(), new DummyPropertyService());
    taskPane.DataContext = vm;

    window.Show();
    window.UpdateLayout();
    taskPane.UpdateLayout();

    taskPane.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    int width = Math.Max((int)taskPane.DesiredSize.Width, 440);
    int height = Math.Max((int)taskPane.DesiredSize.Height, 600);
    taskPane.Arrange(new Rect(0, 0, width, height));

    var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
    bmp.Render(taskPane);

    // Save PNG to a path and return it in screenshot_paths.
}
```

## Capturing evidence

Use the desktop screenshot tool or `RenderTargetBitmap` to capture the rendered window. Save the file under `C:\devin\worktrees\build-issue-{issue}\wpf-smoke\` and return its absolute path in the `screenshot_paths` JSON field.

## What this proves

- XAML layout loads without binding errors.
- ViewModels can be constructed from stubs.
- Commands and property sets behave as expected in a STA thread.

It does **not** prove SolidWorks COM registration, task-pane hosting, or live document-property mapping.
