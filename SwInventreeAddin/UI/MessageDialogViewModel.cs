using System;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// View-model for <see cref="MessageDialog"/>, the reusable owner-centered
    /// message pop-up that replaces WinForms <c>MessageBox</c>.
    /// Pure C# — no WPF window types — so button visibility, icon selection, and
    /// result mapping are fully unit-testable without an STA thread.
    /// </summary>
    public sealed class MessageDialogViewModel
    {
        // ── Bindable properties ───────────────────────────────────────────────

        /// <summary>The dialog title.</summary>
        public string Title { get; }

        /// <summary>The message shown to the engineer.</summary>
        public string Message { get; }

        /// <summary>Segoe MDL2 Assets glyph for the icon, or empty when there is none.</summary>
        public string IconGlyph { get; }

        /// <summary>True when an icon should be shown next to the message.</summary>
        public bool IsIconVisible { get; }

        public bool IsOkVisible     { get; }
        public bool IsCancelVisible { get; }
        public bool IsYesVisible    { get; }
        public bool IsNoVisible     { get; }

        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>The button that was clicked; <see cref="MessageDialogResult.None"/> while open.</summary>
        public MessageDialogResult Result { get; private set; } = MessageDialogResult.None;

        /// <summary>Raised when a button click should close the dialog.</summary>
        public event EventHandler? CloseRequested;

        // ── Constructors ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates the view-model for a message pop-up.
        /// </summary>
        /// <param name="title">The dialog title.</param>
        /// <param name="message">The message shown to the engineer.</param>
        /// <param name="buttons">Which buttons to show, matching the WinForms
        /// <c>MessageBoxButtons</c> names the call sites already use.</param>
        /// <param name="icon">Which icon to show, matching the WinForms
        /// <c>MessageBoxIcon</c> names the call sites already use.</param>
        public MessageDialogViewModel(
            string title,
            string message,
            System.Windows.Forms.MessageBoxButtons buttons,
            System.Windows.Forms.MessageBoxIcon icon)
        {
            Title   = title   ?? throw new ArgumentNullException(nameof(title));
            Message = message ?? throw new ArgumentNullException(nameof(message));

            IsOkVisible     = buttons == System.Windows.Forms.MessageBoxButtons.OK
                           || buttons == System.Windows.Forms.MessageBoxButtons.OKCancel;
            IsCancelVisible = buttons == System.Windows.Forms.MessageBoxButtons.OKCancel
                           || buttons == System.Windows.Forms.MessageBoxButtons.YesNoCancel
                           || buttons == System.Windows.Forms.MessageBoxButtons.RetryCancel
                           || buttons == System.Windows.Forms.MessageBoxButtons.AbortRetryIgnore;
            IsYesVisible    = buttons == System.Windows.Forms.MessageBoxButtons.YesNo
                           || buttons == System.Windows.Forms.MessageBoxButtons.YesNoCancel;
            IsNoVisible     = IsYesVisible;

            IconGlyph     = IconToGlyph(icon);
            IsIconVisible = IconGlyph.Length > 0;
        }

        // ── Commands ──────────────────────────────────────────────────────────

        /// <summary>Records Ok and asks the window to close.</summary>
        public void ClickOk()     => SetResultAndClose(MessageDialogResult.Ok);

        /// <summary>Records Cancel and asks the window to close.</summary>
        public void ClickCancel() => SetResultAndClose(MessageDialogResult.Cancel);

        /// <summary>Records Yes and asks the window to close.</summary>
        public void ClickYes()    => SetResultAndClose(MessageDialogResult.Yes);

        /// <summary>Records No and asks the window to close.</summary>
        public void ClickNo()     => SetResultAndClose(MessageDialogResult.No);

        /// <summary>
        /// Records the result a window close (X button or Esc) should produce —
        /// Cancel when shown, else No, else Ok — matching WinForms MessageBox.
        /// Called by the window's <c>Closing</c> handler; does not raise
        /// <see cref="CloseRequested"/> because the window is already closing.
        /// </summary>
        public void SetCloseResult()
        {
            if (Result != MessageDialogResult.None)
                return;

            Result = IsCancelVisible ? MessageDialogResult.Cancel
                   : IsNoVisible     ? MessageDialogResult.No
                   :                   MessageDialogResult.Ok;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetResultAndClose(MessageDialogResult result)
        {
            Result = result;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private static string IconToGlyph(System.Windows.Forms.MessageBoxIcon icon)
        {
            switch (icon)
            {
                // Segoe MDL2 Assets glyphs, matching the BomTableMissingDialog style.
                // WinForms aliases share values (Warning=Exclamation, Error=Stop=Hand,
                // Information=Asterisk), so one case label per glyph.
                case System.Windows.Forms.MessageBoxIcon.Warning:
                    return "\uE7BA";   // Warning triangle
                case System.Windows.Forms.MessageBoxIcon.Error:
                    return "\uE783";   // Error
                case System.Windows.Forms.MessageBoxIcon.Question:
                    return "\uE897";   // Help
                case System.Windows.Forms.MessageBoxIcon.Information:
                    return "\uE946";   // Info
                default:
                    return string.Empty;
            }
        }
    }
}
