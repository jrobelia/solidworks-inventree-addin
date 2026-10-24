using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using SwInventreeAddin.Config;
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

        private static Window CreateTaskPaneWindow(bool mappingError)
        {
            var mapping = new StubPropertyMappingProvider();
            var properties = new StubDocumentPropertyService();
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

            var window = new Window
            {
                Content = new TaskPaneView { DataContext = pair.ViewModel, Background = SystemColors.ControlBrush },
                Width = 360,
                Height = 720,
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
