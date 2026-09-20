using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests.Capture
{
    // Explicit entry point for the render catalog — renders every registered
    // surface state to <test output>/Captures/<Surface>-<state>.png for agent
    // visual-feedback loops. [Explicit]: never runs in the normal suite. Invoke:
    //   dotnet test ... --filter "Name~Capture_"                  (whole catalog)
    //   dotnet test ... --filter "Name~Capture_SettingsWindow"    (one surface)
    [TestFixture]
    [Explicit]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class SurfaceCaptureTests
    {
        private static IEnumerable<TestCaseData> CatalogCases()
            => SurfaceCapture.Catalog.Select(entry =>
                new TestCaseData(entry).SetName($"Capture_{entry.Surface}_{entry.State}"));

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

        [TestCaseSource(nameof(CatalogCases))]
        public void Capture(SurfaceCapture.Entry entry)
        {
            var path = SurfaceCapture.RenderToPng(entry);

            Assert.That(File.Exists(path), Is.True);
            TestContext.WriteLine(path);
        }
    }
}
