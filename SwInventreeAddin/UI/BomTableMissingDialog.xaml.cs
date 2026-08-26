using System;
using System.Windows;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Modal warning shown when no SolidWorks BOM table matches the configured BOM keyword.
    /// Centers over the supplied owner window using a manual Win32 positioner.
    /// </summary>
    public partial class BomTableMissingDialog : Window
    {
        private readonly IntPtr _ownerHandle;

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

            WindowCentering.Attach(this, _ownerHandle);
        }
    }
}
