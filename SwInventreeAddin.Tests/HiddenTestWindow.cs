using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    /// <summary>
    /// Keeps live test windows off every monitor. Hiding must be positional: WPF
    /// <c>Opacity = 0</c> does not hide a window — it stops the contents rendering and
    /// leaves an unpainted black window on-screen.
    /// </summary>
    internal static class HiddenTestWindow
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

        /// <summary>Returns the native window rectangle for a live handle.</summary>
        internal static Rectangle GetRect(IntPtr hWnd)
        {
            _ = GetWindowRect(hWnd, out var rect);
            return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        /// <summary>
        /// Creates a large owner form parked at the same far off-screen position
        /// production uses, so a dialog centered on it lands outside every monitor.
        /// It cannot be maximized — a maximized window snaps back onto a monitor.
        /// </summary>
        internal static Form CreateOwnerForm()
            => new Form
            {
                StartPosition = FormStartPosition.Manual,
                Left = WindowCentering.HiddenLeft,
                Top = WindowCentering.HiddenTop,
                Width = 2000,
                Height = 1200,
                WindowState = FormWindowState.Normal,
                ShowInTaskbar = false,
            };

        /// <summary>True when the native window rectangle overlaps any display.</summary>
        internal static bool IsOnScreen(Rectangle rect)
        {
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.Bounds.IntersectsWith(rect))
                    return true;
            }
            return false;
        }

        private const int CenteredTolerancePx = 5;
        private const int CenteringPollIntervalMs = 50;

        // The deadline every caller shares: inside the fixtures' [Timeout(10000)]
        // while leaving headroom for a slow first render under load.
        private static readonly TimeSpan DefaultCenteredTimeout = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Polls on the dialog's dispatcher until the dialog's native rectangle is
        /// centered on the owner's (within <see cref="CenteredTolerancePx"/> pixels on
        /// both axes) and still off every monitor, then closes the dialog so a
        /// blocking <c>ShowDialog()</c> unwinds. Faults the task with an
        /// <see cref="AssertionException"/> carrying the last-observed rects when
        /// <see cref="DefaultCenteredTimeout"/> elapses first.
        /// </summary>
        /// <remarks>
        /// Safe to call before the window has a native handle — the poll runs on
        /// <paramref name="dialog"/>'s dispatcher, which only pumps once
        /// <c>ShowDialog()</c> runs. The verdict never depends on a fixed wall-clock
        /// interval racing dispatcher work: every tick re-observes the native rects,
        /// so machine load only delays completion, never changes it. Consume with
        /// <c>wait.GetAwaiter().GetResult()</c> after <c>ShowDialog()</c> returns so
        /// the assertion is not wrapped in an <see cref="AggregateException"/>.
        /// </remarks>
        internal static Task WaitForCenteredOnOwnerAsync(Window dialog, IntPtr ownerHandle)
        {
            var tcs = new TaskCompletionSource<bool>();
            var deadline = DateTime.UtcNow + DefaultCenteredTimeout;
            var helper = new WindowInteropHelper(dialog);

            var timer = new DispatcherTimer(DispatcherPriority.Background, dialog.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(CenteringPollIntervalMs),
            };

            timer.Tick += (s, e) =>
            {
                var ownerRect = GetRect(ownerHandle);
                var dialogRect = GetRect(helper.Handle);

                var dx = Math.Abs(CenterX(dialogRect) - CenterX(ownerRect));
                var dy = Math.Abs(CenterY(dialogRect) - CenterY(ownerRect));

                if (dx < CenteredTolerancePx && dy < CenteredTolerancePx && !IsOnScreen(dialogRect))
                {
                    timer.Stop();
                    TestContext.WriteLine(
                        $"Centered: owner rect {ownerRect}, dialog rect {dialogRect} (off by {dx},{dy} px)");
                    dialog.Close();
                    tcs.SetResult(true);
                    return;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    timer.Stop();
                    dialog.Close();
                    tcs.SetException(new AssertionException(
                        $"Dialog was not centered on its owner within {DefaultCenteredTimeout.TotalSeconds:0.#}s " +
                        $"(last offset {dx},{dy} px). " +
                        $"Owner rect: {ownerRect}. Dialog rect: {dialogRect}."));
                }
            };

            timer.Start();
            return tcs.Task;
        }

        private static int CenterX(Rectangle rect) => rect.Left + rect.Width / 2;

        private static int CenterY(Rectangle rect) => rect.Top + rect.Height / 2;
    }
}
