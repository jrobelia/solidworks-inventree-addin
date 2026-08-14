using System;
using System.Diagnostics;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Holds the SolidWorks main window handle so WPF dialogs can parent
    /// themselves reliably via <see cref="WindowInteropHelper.Owner"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Process.MainWindowHandle"/> is unreliable inside a SolidWorks
    /// add-in — on SolidWorks 2026 it returns <see cref="IntPtr.Zero"/> or a
    /// stale handle, causing modal dialogs to open behind SolidWorks and lock
    /// the application. The handle is set once in <c>SwAddin.ConnectToSW</c>
    /// from the SolidWorks COM API (<c>ISldWorks.IFrameObject.GetHWndx64</c>)
    /// and read by every dialog constructor. If the SW-provided handle is
    /// <see cref="IntPtr.Zero"/> (e.g. during unit tests or an API failure),
    /// <see cref="Get"/> falls back to <see cref="Process.MainWindowHandle"/>
    /// to preserve the previous behaviour.
    /// </remarks>
    public static class SolidWorksWindowHandle
    {
        private static IntPtr _handle = IntPtr.Zero;

        /// <summary>Set the SolidWorks main window handle. Called once during add-in load.</summary>
        public static void Set(IntPtr handle) => _handle = handle;

        /// <summary>
        /// Returns the SolidWorks main window handle, or falls back to
        /// <see cref="Process.MainWindowHandle"/> if no SW handle was set.
        /// </summary>
        public static IntPtr Get()
        {
            if (_handle != IntPtr.Zero)
                return _handle;

            try
            {
                return Process.GetCurrentProcess().MainWindowHandle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }
}
