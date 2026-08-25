using System;
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
        /// <summary>
        /// Creates and shows the warning dialog centered over the supplied owner window.
        /// </summary>
        /// <param name="bomKeyword">The configured BOM keyword the table should have matched.</param>
        /// <param name="ownerHandle">The Win32 window handle to use as the owner for centering.</param>
        public BomTableMissingDialog(string bomKeyword, IntPtr ownerHandle)
        {
            InitializeComponent();

            DataContext = new BomTableMissingViewModel(bomKeyword);

            // Set the owner so WindowStartupLocation="CenterOwner" works.
            try
            {
                var helper = new WindowInteropHelper(this);
                helper.Owner = ownerHandle;
            }
            catch { /* cosmetic only */ }
        }
    }
}
