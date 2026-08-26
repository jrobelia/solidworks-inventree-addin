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
    internal static class WindowCentering
    {
        private const uint SwpNoSize     = 0x0001;
        private const uint SwpNoZOrder   = 0x0004;
        private const uint SwpNoActivate = 0x0010;

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
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Sets the Win32 owner of <paramref name="window"/> to <paramref name="ownerHandle"/>
        /// and arranges for the window to be manually centered on that owner once its
        /// <see cref="Window.SourceInitialized"/> event fires.
        /// </summary>
        public static void Attach(Window window, IntPtr ownerHandle)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            // Set the owner for modality and z-order. If this fails, the window still works.
            try
            {
                var helper = new WindowInteropHelper(window);
                if (ownerHandle != IntPtr.Zero)
                    helper.Owner = ownerHandle;
            }
            catch { /* cosmetic only */ }

            // The captured owner handle will be used in SourceInitialized to center the window.
            window.SourceInitialized += (s, e) => Center(window, ownerHandle);
        }

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

        /// <summary>
        /// Calculates the top-left screen coordinates that center a dialog over its owner.
        /// The dialog's own origin is ignored; only its width and height matter.
        /// </summary>
        internal static (int left, int top) CalculateCenteredPosition(RECT ownerRect, RECT dialogRect)
        {
            int ownerWidth   = ownerRect.Right  - ownerRect.Left;
            int ownerHeight  = ownerRect.Bottom - ownerRect.Top;
            int dialogWidth  = dialogRect.Right - dialogRect.Left;
            int dialogHeight = dialogRect.Bottom - dialogRect.Top;

            int left = ownerRect.Left + (ownerWidth  - dialogWidth)  / 2;
            int top  = ownerRect.Top  + (ownerHeight - dialogHeight) / 2;

            return (left, top);
        }
    }
}
