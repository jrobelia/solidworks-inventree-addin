using System;
using System.Drawing;
using System.IO;
using System.Threading;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class ImageCropWindowTests
    {
        private static readonly string CropBoundsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SwInventreeAddin", "crop_window_bounds.txt");

        [SetUp]
        [TearDown]
        public void ResetState()
        {
            SolidWorksWindowHandle.Set(IntPtr.Zero);
            try { File.Delete(CropBoundsFilePath); } catch { /* non-critical */ }
        }

        [Test, Timeout(10000)]
        public void ShowDialog_CentersOnOwnerWindow()
        {
            using var form = HiddenTestWindow.CreateOwnerForm();
            form.Show();

            SolidWorksWindowHandle.Set(form.Handle);

            using var image = new Bitmap(100, 100);
            var dialog = new ImageCropWindow(image);
            var wait = HiddenTestWindow.WaitForCenteredOnOwnerAsync(dialog, form.Handle);

            dialog.ShowDialog();
            wait.GetAwaiter().GetResult();
        }
    }
}
