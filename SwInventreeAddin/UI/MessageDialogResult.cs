namespace SwInventreeAddin.UI
{
    /// <summary>
    /// The button the engineer clicked on a <see cref="MessageDialog"/>.
    /// <see cref="None"/> means the dialog is still open.
    /// </summary>
    public enum MessageDialogResult
    {
        None,
        Ok,
        Cancel,
        Yes,
        No,
    }
}
