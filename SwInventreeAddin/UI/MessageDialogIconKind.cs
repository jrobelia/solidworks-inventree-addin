namespace SwInventreeAddin.UI
{
    /// <summary>
    /// The icon a <see cref="MessageDialog"/> shows, independent of how callers
    /// asked for it. Drives the glyph and severity colour in XAML.
    /// </summary>
    public enum MessageDialogIconKind
    {
        None,
        Information,
        Question,
        Warning,
        Error,
    }
}
