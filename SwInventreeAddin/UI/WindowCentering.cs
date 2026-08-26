using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Centers a WPF <see cref="Window"/> over an arbitrary Win32 owner window using
    /// native screen coordinates. This works around WPF <c>CenterOwner</c> not correctly
    /// centering on a non-WPF, maximised, multi-monitor / high-DPI owner.
    /// </summary>
    public static class WindowCentering
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const uint SwpNoSize     = 0x0001;
        private const uint SwpNoZOrder   = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        // ── Native interop ────────────────────────────────────────────────────

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

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
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // ── Public interface ──────────────────────────────────────────────────

        /// <summary>
        /// Sets the Win32 owner of <paramref name="window"/> to <paramref name="ownerHandle"/>
        /// and centers the window on that owner when its content is first rendered.
        /// </summary>
        /// <remarks>
        /// Centering is deferred until <see cref="Window.ContentRendered"/> so the final
        /// size is known for <c>SizeToContent</c> dialogs. The window starts at
        /// <c>Opacity="0"</c> and is revealed only after positioning, avoiding a flash.
        /// </remarks>
        public static void Attach(Window window, IntPtr ownerHandle)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            try
            {
                var helper = new WindowInteropHelper(window);
                if (ownerHandle != IntPtr.Zero)
                    helper.Owner = ownerHandle;
            }
            catch { /* cosmetic only */ }

            EventHandler? handler = null;
            handler = (s, e) =>
            {
                if (s is Window w)
                {
                    w.ContentRendered -= handler!;
                    Center(w, ownerHandle);
                }
            };
            window.ContentRendered += handler;
        }

        /// <summary>
        /// Calculates the top-left screen coordinates that center a dialog over its owner.
        /// The dialog's own origin is ignored; only its width and height matter.
        /// </summary>
        public static (int left, int top) CalculateCenteredPosition(RECT ownerRect, RECT dialogRect)
        {
            int ownerWidth   = ownerRect.Right  - ownerRect.Left;
            int ownerHeight  = ownerRect.Bottom - ownerRect.Top;
            int dialogWidth  = dialogRect.Right - dialogRect.Left;
            int dialogHeight = dialogRect.Bottom - dialogRect.Top;

            int left = ownerRect.Left + (ownerWidth  - dialogWidth)  / 2;
            int top  = ownerRect.Top  + (ownerHeight - dialogHeight) / 2;

            return (left, top);
        }

        // ── Private implementation ────────────────────────────────────────────

        private static void Center(Window window, IntPtr ownerHandle)
        {
            try
            {
                var dialogHandle = new WindowInteropHelper(window).Handle;
                if (ownerHandle == IntPtr.Zero || dialogHandle == IntPtr.Zero ||
                    !GetWindowRect(ownerHandle, out var ownerRect) ||
                    !GetWindowRect(dialogHandle, out var dialogRect))
                    return;

                var (left, top) = CalculateCenteredPosition(ownerRect, dialogRect);

                _ = SetWindowPos(
                    dialogHandle,
                    IntPtr.Zero,
                    left,
                    top,
                    0,
                    0,
                    SwpNoSize | SwpNoZOrder | SwpNoActivate);
            }
            finally
            {
                // Reveal the window once it has been positioned. Starting at Opacity 0
                // avoids a brief flash at a wrong location before the manual centering runs.
                window.Opacity = 1.0;
            }
        }
    }
}
