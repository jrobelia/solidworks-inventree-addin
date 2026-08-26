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

        /// <summary>
        /// Creates the confirmation dialog.
        /// </summary>
        /// <param name="message">The question shown to the user.</param>
        /// <param name="imageCheckedByDefault">Whether the "Also push image" checkbox starts ticked.</param>
        public PushRevisionConfirmDialog(string message, bool imageCheckedByDefault = true)
        {
            InitializeComponent();

            MessageText.Text                  = message;
            IncludeImageCheckBox.IsChecked    = imageCheckedByDefault;

            // Parent and center over the SolidWorks main window.
            WindowCentering.Attach(this, SolidWorksWindowHandle.Get());
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
