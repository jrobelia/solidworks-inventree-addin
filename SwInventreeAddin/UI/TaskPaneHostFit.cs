using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Keeps the hosted Task Pane control fitted to its native parent window.
    /// </summary>
    /// <remarks>
    /// SolidWorks re-parents the control's HWND into the Task Pane via
    /// <c>DisplayWindowFromHandlex64</c>, but on some host paths — observed when the
    /// add-in is enabled mid-session — the pane never pushes its client rectangle into
    /// the child, leaving the WPF surface rendering inside a creation-size square in
    /// the pane's upper-left while the previously shown tab stays visible behind it
    /// (issue #307). The host cannot be relied on to send WM_SIZE, so the control
    /// re-asserts its bounds itself whenever the host next paints or shows it.
    /// </remarks>
    internal static class TaskPaneHostFit
    {
        // ── Native interop ────────────────────────────────────────────────────

        private const uint SwpNoActivate = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // ── Interface ─────────────────────────────────────────────────────────

        /// <summary>
        /// Subscribes <paramref name="control"/> to the host paint and visibility
        /// events that reveal a misfit, so the window corrects itself the next time
        /// SolidWorks shows or repaints it. Subscriptions last the control's lifetime.
        /// </summary>
        internal static void Attach(Control control)
        {
            if (control == null)
                throw new ArgumentNullException(nameof(control));

            control.Paint += OnHostSurfaceEvent;
            control.VisibleChanged += OnHostSurfaceEvent;
        }

        /// <summary>
        /// One fit pass: when <paramref name="control"/>'s HWND does not fill its
        /// native parent's client area, resize it to fill and request a repaint —
        /// a no-op when the bounds already match. Called by the wired host events;
        /// internal so tests can drive the same mechanism without a
        /// SolidWorks-hosted parent.
        /// </summary>
        internal static void EnsureHostFit(Control control)
        {
            if (control.IsDisposed || control.Disposing || !control.IsHandleCreated)
                return;

            IntPtr hwnd = control.Handle;
            IntPtr parent = GetParent(hwnd);
            if (parent == IntPtr.Zero)
                return;
            if (!GetClientRect(parent, out var rect))
                return;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                return;

            var expected = new Rectangle(0, 0, width, height);
            if (control.Bounds == expected)
                return;

            // A zero insert-after handle is HWND_TOP: besides resizing, this lifts
            // the control above a previously shown tab the host failed to hide.
            _ = SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, SwpNoActivate);
            control.Invalidate();
        }

        private static void OnHostSurfaceEvent(object? sender, EventArgs e)
        {
            try
            {
                if (sender is Control control)
                    EnsureHostFit(control);
            }
            catch { /* cosmetic only — a failed self-heal must never escape a host paint */ }
        }
    }
}
