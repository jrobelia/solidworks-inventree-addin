using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Reusable owner-centered message pop-up that replaces WinForms
    /// <c>MessageBox</c>. WPF windows parented with
    /// <see cref="WindowCentering"/> center on the owner's real Win32 bounds,
    /// which WinForms <c>MessageBox.Show(IWin32Window, ...)</c> cannot do.
    /// </summary>
    /// <remarks>
    /// The window is hidden by position, not opacity — see <see cref="WindowCentering"/>.
    /// Use the static <see cref="ShowOK"/>, <see cref="ShowOKCancel"/>, and
    /// <see cref="ShowYesNo"/> helpers; they keep the same owner/message/title/icon
    /// call shape the old <c>MessageBox.Show</c> call sites used.
    /// </remarks>
    public partial class MessageDialog : Window
    {
        private readonly MessageDialogViewModel _viewModel;

        /// <summary>
        /// Creates the dialog. The window stays hidden off-screen until
        /// <see cref="WindowCentering"/> moves it, centered on
        /// <paramref name="ownerHandle"/>.
        /// </summary>
        /// <param name="viewModel">Title, message, icon, and button layout.</param>
        /// <param name="ownerHandle">Win32 handle of the window to center over —
        /// the SolidWorks main window or the add-in window that spawned the pop-up.</param>
        public MessageDialog(MessageDialogViewModel viewModel, IntPtr ownerHandle)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            InitializeComponent();

            DataContext = _viewModel;
            _viewModel.CloseRequested += (s, e) => Close();

            WindowCentering.Attach(this, ownerHandle);
        }

        /// <summary>Shows the dialog modally and returns the clicked result.</summary>
        public new MessageDialogResult ShowDialog()
        {
            base.ShowDialog();
            return _viewModel.Result;
        }

        // ── Static helpers ────────────────────────────────────────────────────

        /// <summary>Shows an OK-only dialog centered on the owner.</summary>
        public static MessageDialogResult ShowOK(
            IntPtr ownerHandle, string message, string title,
            System.Windows.Forms.MessageBoxIcon icon)
            => Show(ownerHandle, message, title, System.Windows.Forms.MessageBoxButtons.OK, icon);

        /// <summary>Shows an OK/Cancel dialog centered on the owner.</summary>
        public static MessageDialogResult ShowOKCancel(
            IntPtr ownerHandle, string message, string title,
            System.Windows.Forms.MessageBoxIcon icon)
            => Show(ownerHandle, message, title, System.Windows.Forms.MessageBoxButtons.OKCancel, icon);

        /// <summary>Shows a Yes/No dialog centered on the owner.</summary>
        public static MessageDialogResult ShowYesNo(
            IntPtr ownerHandle, string message, string title,
            System.Windows.Forms.MessageBoxIcon icon)
            => Show(ownerHandle, message, title, System.Windows.Forms.MessageBoxButtons.YesNo, icon);

        private static MessageDialogResult Show(
            IntPtr ownerHandle, string message, string title,
            System.Windows.Forms.MessageBoxButtons buttons,
            System.Windows.Forms.MessageBoxIcon icon)
            => new MessageDialog(
                new MessageDialogViewModel(title, message, buttons, icon),
                ownerHandle).ShowDialog();

        // ── Event handlers ────────────────────────────────────────────────────

        private void Ok_Click(object sender, RoutedEventArgs e)     => _viewModel.ClickOk();
        private void Cancel_Click(object sender, RoutedEventArgs e) => _viewModel.ClickCancel();
        private void Yes_Click(object sender, RoutedEventArgs e)    => _viewModel.ClickYes();
        private void No_Click(object sender, RoutedEventArgs e)     => _viewModel.ClickNo();

        /// <summary>
        /// The X button closes the window without a click — record the
        /// cancel-equivalent result (Cancel, else No, else Ok) like WinForms.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            _viewModel.SetCloseResult();
            base.OnClosing(e);
        }

        /// <summary>Esc closes the dialog with the same result as the X button.</summary>
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            base.OnPreviewKeyDown(e);
        }
    }
}
