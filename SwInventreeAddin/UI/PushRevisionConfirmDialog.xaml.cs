using System.Windows;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Confirmation dialog shown before pushing a SolidWorks field value to
    /// InvenTree. Shared by every per-field Push on the Task Pane — the caller
    /// supplies the message and the action title. Includes a checkbox so the
    /// user can optionally push an image at the same time.
    /// </summary>
    public partial class PushRevisionConfirmDialog : Window
    {
        /// <summary>True when the user ticked "Also push image to InvenTree".</summary>
        public bool IncludeImage { get; private set; }

        /// <summary>
        /// Creates the confirmation dialog.
        /// </summary>
        /// <param name="message">The question shown to the user.</param>
        /// <param name="title">The action the dialog confirms — shown in the
        /// banded header and the window title (e.g. "Push Revision").</param>
        /// <param name="imageCheckedByDefault">Whether the "Also push image" checkbox starts ticked.</param>
        public PushRevisionConfirmDialog(string message, string title, bool imageCheckedByDefault = true)
        {
            InitializeComponent();

            MessageText.Text = message;
            HeaderTitleText.Text = title;
            Title = title;
            IncludeImageCheckBox.IsChecked = imageCheckedByDefault;

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
