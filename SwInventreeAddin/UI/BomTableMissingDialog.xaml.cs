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
        private readonly IntPtr _ownerHandle;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

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

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_ownerHandle == IntPtr.Zero || !GetWindowRect(_ownerHandle, out var ownerRect))
                    return;

                uint dpi;
                try
                {
                    dpi = GetDpiForWindow(_ownerHandle);
                }
                catch (EntryPointNotFoundException)
                {
                    dpi = 96;
                }

                if (dpi == 0)
                    dpi = 96;

                double scale = dpi / 96.0;
                double centerX = (ownerRect.Left + ownerRect.Right) / 2.0;
                double centerY = (ownerRect.Top + ownerRect.Bottom) / 2.0;

                // WPF Left/Top are device-independent units. Convert from the owner's
                // monitor pixels (which are what GetWindowRect returns) using the
                // owner's monitor scale, then subtract half the dialog size in DIPs.
                Left = (centerX / scale) - (ActualWidth / 2.0);
                Top  = (centerY / scale) - (ActualHeight / 2.0);
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
