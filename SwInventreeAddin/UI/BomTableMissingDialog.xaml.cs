using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Modal warning shown when no SolidWorks BOM table matches the configured BOM keyword.
    /// Centers over the SolidWorks main window.
    /// </summary>
    public partial class BomTableMissingDialog : Window
    {
        private const uint SwpNoSize     = 0x0001;
        private const uint SwpNoZOrder   = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        private readonly IntPtr _ownerHandle;

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
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Creates and shows the warning dialog centered over the supplied owner window.
        /// </summary>
        /// <param name="bomKeyword">The configured BOM keyword the table should have matched.</param>
        /// <param name="ownerHandle">The Win32 window handle to use as the owner for centering.</param>
        public BomTableMissingDialog(string bomKeyword, IntPtr ownerHandle)
        {
            _ownerHandle = ownerHandle;

            InitializeComponent();

            DataContext = new BomTableMissingViewModel(bomKeyword);

            // Set the owner so the dialog is modal and follows the owner window.
            try
            {
                var helper = new WindowInteropHelper(this);
                helper.Owner = ownerHandle;
            }
            catch { /* cosmetic only */ }

            SourceInitialized += OnSourceInitialized;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            try
            {
                var dialogHandle = new WindowInteropHelper(this).Handle;
                if (_ownerHandle == IntPtr.Zero || dialogHandle == IntPtr.Zero ||
                    !GetWindowRect(_ownerHandle, out var ownerRect) ||
                    !GetWindowRect(dialogHandle, out var dialogRect))
                    return;

                int ownerWidth   = ownerRect.Right - ownerRect.Left;
                int ownerHeight  = ownerRect.Bottom - ownerRect.Top;
                int dialogWidth  = dialogRect.Right - dialogRect.Left;
                int dialogHeight = dialogRect.Bottom - dialogRect.Top;
                int left         = ownerRect.Left + (ownerWidth - dialogWidth) / 2;
                int top          = ownerRect.Top + (ownerHeight - dialogHeight) / 2;

                // Keep the centering calculation and movement in one native coordinate
                // system so mixed-DPI monitor origins are not converted as WPF DIPs.
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
                Opacity = 1.0;
            }
        }
    }
}
