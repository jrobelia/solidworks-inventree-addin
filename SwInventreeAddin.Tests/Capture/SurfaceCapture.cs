using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NUnit.Framework;
using SwInventreeAddin.Bom;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests.Capture
{
    /// <summary>
    /// Render catalog for agent visual-feedback loops. Each <see cref="Entry"/>
    /// builds one add-in surface in a named state; <see cref="RenderToPng"/> writes
    /// it to <c>&lt;test output&gt;/Captures/&lt;Surface&gt;-&lt;state&gt;.png</c>.
    /// Runs only through the [Explicit] <c>SurfaceCaptureTests</c> fixture:
    /// <c>dotnet test --filter "Name~Capture_"</c> renders the whole catalog, and
    /// <c>"Name~Capture_&lt;Surface&gt;"</c> scopes to the surfaces a diff touched.
    /// Adding a surface or state is additive: one <see cref="Entry"/> plus its
    /// stub wiring — the render plumbing here does not change.
    /// </summary>
    public static class SurfaceCapture
    {
        public sealed class Entry
        {
            public Entry(string surface, string state, Func<Window> create)
            {
                Surface = surface;
                State = state;
                Create = create;
            }

            /// <summary>The surface under capture, e.g. "SettingsWindow".</summary>
            public string Surface { get; }

            /// <summary>The named state the surface is driven into, e.g. "fresh".</summary>
            public string State { get; }

            /// <summary>
            /// Builds the surface on the test stubs and drives it into
            /// <see cref="State"/>. Runs on the STA thread inside the capture test.
            /// </summary>
            public Func<Window> Create { get; }
        }

        public static IReadOnlyList<Entry> Catalog { get; } = new[]
        {
            new Entry("SettingsWindow", "fresh",
                () => CreateSettingsWindow(StubConfigProvider.WithNoSavedConfig())),
            new Entry("SettingsWindow", "configured",
                () => CreateSettingsWindow(SavedConfig())),
            new Entry("SettingsWindow", "change-credential", () =>
            {
                var window = CreateSettingsWindow(SavedConfig());
                var button = (ButtonBase)LogicalTreeHelper.FindLogicalNode(window, "ChangeCredentialButton")!;
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                return window;
            }),
            // #342: a shared file is applied, a different one is browsed into
            // the path field but never applied — Edit Mappings is disabled.
            new Entry("SettingsWindow", "shared-draft-unapplied", () =>
            {
                var config = SavedConfig();
                config.Config!.MappingSourcePath = "\\\\share\\map-a.json";
                var sharedProvider = new StubPropertyMappingProvider
                {
                    SourceFilePath = "\\\\share\\map-a.json",
                    SourceFileExists = true,
                };
                var window = CreateSettingsWindow(config, sharedProvider);
                ((TextBox)LogicalTreeHelper.FindLogicalNode(window, "SharedPathBox")!).Text =
                    "\\\\share\\map-b.json";
                return window;
            }),
            new Entry("TaskPaneView", "suppressed-error", () => CreateTaskPaneWindow(false)),
            new Entry("TaskPaneView", "mapping-error", () => CreateTaskPaneWindow(true)),
            new Entry("TaskPaneView", "assembly",
                () => CreateTaskPaneWindow(false, DocumentType.Assembly, height: 1160)),
            new Entry("TaskPaneView", "connection-failed",
                () => CreateTaskPaneWindowWithConnectionFailure()),
            // #334 audit surfaces — one representative state per shipped window.
            new Entry("PropertyMappingEditorWindow", "default",
                () => new PropertyMappingEditorWindow(new StubPropertyMappingProvider())),
            new Entry("PushRevisionConfirmDialog", "default",
                () => new PushRevisionConfirmDialog(
                    "Push revision \"B\" of CAP-100 (Bracket, Front) to InvenTree?\r\n\r\n" +
                    "The InvenTree revision will be updated to \"B\".")),
            new Entry("BomCompareWindow", "loaded", CreateBomCompareWindow),
            new Entry("CreatePartWindow", "categories-loaded", CreateCreatePartWindow),
            new Entry("ImageCropWindow", "default",
                () => new ImageCropWindow(new System.Drawing.Bitmap(480, 360))),
            new Entry("MessageDialog", "question-yesno",
                () => new MessageDialog(
                    new MessageDialogViewModel(
                        "Confirm BOM Push",
                        "Push 2 new line(s) and update 1 conflict(s) to InvenTree?",
                        System.Windows.Forms.MessageBoxButtons.YesNo,
                        System.Windows.Forms.MessageBoxIcon.Question),
                    SolidWorksWindowHandle.Get())),
            new Entry("BomTableMissingDialog", "default",
                () => new BomTableMissingDialog("inventree", SolidWorksWindowHandle.Get())),
        };

        public static string PngPathFor(string surface, string state)
            => Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "Captures",
                $"{surface}-{state}.png");

        public static string RenderToPng(Entry entry)
        {
            var path = PngPathFor(entry.Surface, entry.State);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var window = entry.Create();
            try
            {
                window.Show();
                window.UpdateLayout();

                var rect = HiddenTestWindow.GetRect(new WindowInteropHelper(window).Handle);
                Assert.That(HiddenTestWindow.IsOnScreen(rect), Is.False,
                    $"{entry.Surface}:{entry.State} must render off every monitor (rect {rect})");

                var content = (FrameworkElement)window.Content!;
                var width = (int)Math.Ceiling(content.ActualWidth);
                var height = (int)Math.Ceiling(content.ActualHeight);
                var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(content);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var stream = File.Create(path);
                encoder.Save(stream);
            }
            finally
            {
                window.Close();
            }

            return path;
        }

        private static Window CreateTaskPaneWindow(
            bool mappingError, DocumentType documentType = DocumentType.Part, double height = 720)
        {
            var mapping = new StubPropertyMappingProvider();
            var properties = new StubDocumentPropertyService
            {
                DocumentTypeToReturn = documentType,
            };
            var part = new InvenTree.InventreePart { Pk = 42, Ipn = "CAPTURE", Name = "Captured part" };
            properties.Seed(mapping.Config.IpnProperty!, part.Ipn);
            var client = new StubInventreeClient { PartToReturn = part };
            if (mappingError)
                mapping.Health = MappingHealth.Invalid;
            var pair = VmFactory.Create(client, properties, mapping, dispatcher: new StubHostStaDispatcher());
            if (!mappingError)
            {
                pair.ViewModel.FetchPartAsync().GetAwaiter().GetResult();
                client.ThrowOnUpdate = new InvalidOperationException("InvenTree could not accept Name.");
                pair.ViewModel.PushNameToInvenTreeAsync().GetAwaiter().GetResult();
                client.ThrowOnUpdate = null;
                pair.ViewModel.PushNotesToInvenTreeAsync().GetAwaiter().GetResult();
            }

            return WrapTaskPaneView(pair, height);
        }

        // BomCompareWindow starts its load in Window_Loaded; the extra Loaded
        // handler below pumps the dispatcher until a fresh LoadAsync completes,
        // so the loaded grid and empty status bar are settled before the
        // RenderToPng snapshot — not the "Loading…" interim state.
        private static Window CreateBomCompareWindow()
        {
            var bomService = new StubAssemblyBomService
            {
                LinesToReturn =
                {
                    // One row per diff state so every row colour in the mock
                    // renders in a single capture.
                    new SwBomLine { SubPartPk = 101, Ipn = "SW-001", Quantity = 2, Reference = "BRK-101" },
                    new SwBomLine { SubPartPk = 102, Ipn = "SW-002", Quantity = 4, Reference = "CAP-10uF" },
                    new SwBomLine { SubPartPk = 103, Ipn = "SW-003", Quantity = 1, Reference = "RES-1k", Note = "new line" },
                    new SwBomLine { Quantity = 2, Reference = "MISC-HDW" },
                    new SwBomLine { Ipn = "SW-404", Quantity = 1, Reference = "GHOST" },
                    new SwBomLine { SubPartPk = 105, Ipn = "SW-005", UnparsedQuantity = "abc", Reference = "QTY?" },
                },
            };
            var client = new StubInventreeClient
            {
                BomLinesToReturn = new List<InventreeBomLine>
                {
                    new InventreeBomLine { Pk = 11, SubPartPk = 101, SubPartIpn = "SW-001", Quantity = 2, Reference = "BRK-101" },
                    new InventreeBomLine { Pk = 12, SubPartPk = 102, SubPartIpn = "SW-002", Quantity = 1, Reference = "CAP-10uF", Consumable = true },
                    new InventreeBomLine { Pk = 13, SubPartPk = 104, SubPartIpn = "IT-900", Quantity = 1, Reference = "EXTRA", Optional = true },
                    new InventreeBomLine { Pk = 14, SubPartPk = 105, SubPartIpn = "SW-005", Quantity = 3, Reference = "QTY?" },
                },
            };
            var vm = new BomCompareViewModel(
                client, bomService, PropertyMappingConfig.WithDefaults(), 42);
            var window = new BomCompareWindow(vm, "ASM-100", "Bracket Assembly", "BOM1")
            {
                // An activated window parked fully off-screen gets snapped back
                // onto a monitor once a message pump runs — keep this one
                // inactive so the Loaded pump below leaves it parked.
                ShowActivated = false,
            };
            window.Loaded += (_, __) =>
            {
                PumpUntilComplete(vm.LoadAsync());
                window.Left = WindowCentering.HiddenLeft;
                window.Top = WindowCentering.HiddenTop;
            };
            return window;
        }

        private static Window CreateCreatePartWindow()
        {
            var client = new StubInventreeClient
            {
                CategoriesToReturn = new List<InventreeCategory>
                {
                    new InventreeCategory { Pk = 1, Name = "Mechanical", HasChildren = true },
                    new InventreeCategory { Pk = 2, Name = "Electronics", HasChildren = false },
                    new InventreeCategory { Pk = 3, Name = "Fasteners", HasChildren = false },
                },
            };
            var vm = new CreatePartViewModel(
                client,
                new StubCreatePartValidationErrorService(),
                "Bracket, Front",
                new StubPropertyMappingProvider(),
                documentType: DocumentType.Part);
            var window = new CreatePartWindow();
            window.Initialise(vm);
            return window;
        }

        /// <summary>
        /// Pumps the dispatcher until <paramref name="task"/> finishes, then
        /// rethrows any fault. LoadAsync marshals its collection updates through
        /// SynchronizationContext.Send — a nested frame delivers them while the
        /// caller stays on the UI thread, so the surface is settled before the
        /// capture snapshot.
        /// </summary>
        private static void PumpUntilComplete(Task task)
        {
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            task.GetAwaiter().GetResult();
        }

        private static Window CreateTaskPaneWindowWithConnectionFailure()
        {
            var pair = VmFactory.Create(
                new StubInventreeClient(),
                new StubDocumentPropertyService(),
                dispatcher: new StubHostStaDispatcher());
            pair.ProjectConnectionState(ServerConnectionStatus.From(
                new ServerConfig
                {
                    Url = "https://inventree.example.com",
                    ApiKey = "saved-key",
                },
                new ConnectionProbeResult(
                    ConnectionProbeStatus.Unreachable,
                    "Could not reach the InvenTree server.")));
            return WrapTaskPaneView(pair);
        }

        private static Window WrapTaskPaneView(TaskPanePair pair, double height = 720)
        {
            var window = new Window
            {
                Content = new TaskPaneView { DataContext = pair.ViewModel, Background = SystemColors.ControlBrush },
                Width = 360,
                Height = height,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            WindowCentering.Attach(window, SolidWorksWindowHandle.Get());
            window.Closed += (_, __) =>
            {
                pair.ViewModel.Dispose();
                pair.Coordinator.Dispose();
            };
            return window;
        }

        private static StubConfigProvider SavedConfig()
            => new StubConfigProvider("https://inventree.example.com", "saved-key");

        private static SettingsWindow CreateSettingsWindow(
            IConfigProvider configProvider, IPropertyMappingProvider? mappingProvider = null)
        {
            mappingProvider ??= new StubPropertyMappingProvider();
            return new SettingsWindow(
                configProvider,
                mappingProvider,
                new StubVersionInfo(),
                new StubSettingsApplyService(),
                new StubMappingProviderFactory { Factory = _ => mappingProvider });
        }
    }
}
