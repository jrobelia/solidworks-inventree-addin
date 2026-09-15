using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
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
        internal static bool IsOnScreen(int left, int top, int right, int bottom)
        {
            var rect = new Rectangle(left, top, right - left, bottom - top);
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.Bounds.IntersectsWith(rect))
                    return true;
            }
            return false;
        }
    }
}
