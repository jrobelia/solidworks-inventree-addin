using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    // Screenshot harness for agent visual-feedback loops — renders named
    // window states to PNG for design-language review. [Explicit]: never runs
    // in the normal suite; invoke with --filter "FullyQualifiedName~Capture_".
    [TestFixture]
    [Explicit]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class SettingsWindowScreenshotTests
    {
        private static readonly string OutDir =
            Path.Combine("C:\\SoftwareProjects", "fix-243-screens");

        private System.Windows.Forms.Form? _ownerForm;

        [SetUp]
        public void SetUp()
        {
            _ownerForm = HiddenTestWindow.CreateOwnerForm();
            _ownerForm.Show();
            SolidWorksWindowHandle.Set(_ownerForm.Handle);
        }

        [TearDown]
        public void TearDown()
        {
            _ownerForm?.Dispose();
        }

        [Test]
        public void Capture_FreshState()
        {
            var window = CreateWindow(StubConfigProvider.WithNoSavedConfig());
            Save(window, "fresh-state.png");
        }

        [Test]
        public void Capture_ConfiguredState()
        {
            var window = CreateWindow(new StubConfigProvider("https://inventree.example.com", "saved-key"));
            Save(window, "configured-card.png");
        }

        [Test]
        public void Capture_ChangeCredentialSlice()
        {
            var window = CreateWindow(new StubConfigProvider("https://inventree.example.com", "saved-key"));
            var button = (ButtonBase)LogicalTreeHelper.FindLogicalNode(window, "ChangeCredentialButton")!;
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Save(window, "change-credential.png");
        }

        private static SettingsWindow CreateWindow(IConfigProvider configProvider)
        {
            var mappingProvider = new StubPropertyMappingProvider();
            return new SettingsWindow(
                configProvider,
                mappingProvider,
                new StubVersionInfo(),
                new StubSettingsApplyService(),
                new StubMappingProviderFactory { Factory = _ => mappingProvider });
        }

        private static void Save(Window window, string fileName)
        {
            Directory.CreateDirectory(OutDir);

            window.Show();
            window.UpdateLayout();

            var content = (FrameworkElement)window.Content!;
            var width = (int)Math.Ceiling(content.ActualWidth);
            var height = (int)Math.Ceiling(content.ActualHeight);
            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(content);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var stream = File.Create(Path.Combine(OutDir, fileName));
            encoder.Save(stream);

            window.Close();
        }
    }
}
