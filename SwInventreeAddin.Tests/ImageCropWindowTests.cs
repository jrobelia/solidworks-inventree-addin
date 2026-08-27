using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class ImageCropWindowTests
    {
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

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
            using var form = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Left = 100,
                Top = 100,
                Width = 1000,
                Height = 1000,
                WindowState = FormWindowState.Maximized,
                ShowInTaskbar = false,
                Opacity = 0,
            };
            form.Show();

            SolidWorksWindowHandle.Set(form.Handle);

            using var image = new Bitmap(100, 100);
            var dialog = new ImageCropWindow(image);
            var tcs = new TaskCompletionSource<bool>();

            dialog.ContentRendered += (s, e) =>
            {
                var timer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(300),
                };

                timer.Tick += (s2, e2) =>
                {
                    timer.Stop();

                    _ = GetWindowRect(form.Handle, out var ownerRect);
                    var helper = new WindowInteropHelper(dialog);
                    _ = GetWindowRect(helper.Handle, out var dialogRect);

                    var ownerCenterX = ownerRect.Left + (ownerRect.Right - ownerRect.Left) / 2;
                    var ownerCenterY = ownerRect.Top + (ownerRect.Bottom - ownerRect.Top) / 2;
                    var dialogCenterX = dialogRect.Left + (dialogRect.Right - dialogRect.Left) / 2;
                    var dialogCenterY = dialogRect.Top + (dialogRect.Bottom - dialogRect.Top) / 2;

                    TestContext.WriteLine($"Owner rect: {ownerRect.Left},{ownerRect.Top},{ownerRect.Right},{ownerRect.Bottom}");
                    TestContext.WriteLine($"Dialog rect: {dialogRect.Left},{dialogRect.Top},{dialogRect.Right},{dialogRect.Bottom}");
                    TestContext.WriteLine($"Owner center: {ownerCenterX},{ownerCenterY}");
                    TestContext.WriteLine($"Dialog center: {dialogCenterX},{dialogCenterY}");

                    var dx = Math.Abs(dialogCenterX - ownerCenterX);
                    var dy = Math.Abs(dialogCenterY - ownerCenterY);

                    try
                    {
                        Assert.That(dx, Is.LessThan(5), $"Dialog is horizontally off by {dx} pixels");
                        Assert.That(dy, Is.LessThan(5), $"Dialog is vertically off by {dy} pixels");
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }

                    dialog.Close();
                    form.Close();
                };

                timer.Start();
            };

            dialog.ShowDialog();
            tcs.Task.Wait();
        }
    }
}
