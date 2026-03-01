using System.Windows;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Confirmation dialog shown before pushing a revision to InvenTree.
    /// Includes a checkbox so the user can optionally push an image at the same time.
    /// </summary>
    public partial class PushRevisionConfirmDialog : Window
    {
        /// <summary>True when the user ticked "Also push image to InvenTree".</summary>
        public bool IncludeImage { get; private set; }

        public PushRevisionConfirmDialog()
        {
            InitializeComponent();

            // Attempt to set SolidWorks as the owner window so the dialog
            // centres over it rather than the primary monitor centre.
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch { /* cosmetic only */ }
        }

        private void Push_Click(object sender, RoutedEventArgs e)
        {
            IncludeImage = IncludeImageCheckBox.IsChecked == true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
