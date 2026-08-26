using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class WindowCenteringTests
    {
        [Test]
        public void CalculateCenteredPosition_DialogFitsInOwner_ReturnsCenter()
        {
            var owner = new WindowCentering.NativeRect { Left = 100, Top = 100, Right = 500, Bottom = 400 };
            var dialog = new WindowCentering.NativeRect { Left = 0, Top = 0, Right = 200, Bottom = 100 };

            var (left, top) = WindowCentering.CalculateCenteredPosition(owner, dialog);

            Assert.That(left, Is.EqualTo(200));
            Assert.That(top, Is.EqualTo(200));
        }

        [Test]
        public void CalculateCenteredPosition_NegativeOwnerOrigin_ReturnsCenter()
        {
            var owner = new WindowCentering.NativeRect { Left = -200, Top = -100, Right = 200, Bottom = 200 };
            var dialog = new WindowCentering.NativeRect { Left = 0, Top = 0, Right = 200, Bottom = 100 };

            var (left, top) = WindowCentering.CalculateCenteredPosition(owner, dialog);

            Assert.That(left, Is.EqualTo(-100));
            Assert.That(top, Is.EqualTo(0));
        }

        [Test]
        public void CalculateCenteredPosition_OwnerSmallerThanDialog_ReturnsCenter()
        {
            var owner = new WindowCentering.NativeRect { Left = 50, Top = 50, Right = 150, Bottom = 150 };
            var dialog = new WindowCentering.NativeRect { Left = 0, Top = 0, Right = 300, Bottom = 200 };

            var (left, top) = WindowCentering.CalculateCenteredPosition(owner, dialog);

            Assert.That(left, Is.EqualTo(-50));
            Assert.That(top, Is.EqualTo(0));
        }

        [Test]
        public void CalculateCenteredPosition_DialogAtNonZeroOrigin_IgnoresDialogOrigin()
        {
            var owner = new WindowCentering.NativeRect { Left = 100, Top = 100, Right = 500, Bottom = 400 };
            var dialog = new WindowCentering.NativeRect { Left = 20, Top = 30, Right = 220, Bottom = 130 };

            var (left, top) = WindowCentering.CalculateCenteredPosition(owner, dialog);

            Assert.That(left, Is.EqualTo(200));
            Assert.That(top, Is.EqualTo(200));
        }
    }

    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class WindowCenteringIntegrationTests
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

        [Test, Timeout(10000)]
        public void Attach_CentersResizableWindowOnOwner()
        {
            using var form = CreateOwnerForm();
            form.Show();

            var dialog = new Window
            {
                Title = "Resizable Dialog",
                Width = 600,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false,
                Opacity = 0,
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = "Test",
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                },
            };

            RunCenteringTest(dialog, form);
        }

        [Test, Timeout(10000)]
        public void Attach_CentersNoResizeWindowOnOwner()
        {
            using var form = CreateOwnerForm();
            form.Show();

            var dialog = new Window
            {
                Title = "NoResize Dialog",
                Width = 500,
                Height = 350,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Opacity = 0,
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = "Test",
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                },
            };

            RunCenteringTest(dialog, form);
        }

        private static Form CreateOwnerForm()
            => new Form
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

        private static void RunCenteringTest(Window dialog, Form form)
        {
            WindowCentering.Attach(dialog, form.Handle);
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
