namespace SwInventreeAddin.Config
{
    /// <summary>
    /// The dot state on the Settings status card. Colour is a XAML resource lookup
    /// keyed off this value, so colour never carries meaning alone — the card's
    /// <see cref="ServerConnectionStatus.Title"/> names every state in text.
    /// </summary>
    public enum ServerConnectionIndicator
    {
        /// <summary>A credential is needed: none saved, or the saved key was rejected. Amber.</summary>
        AuthenticationRequired,

        /// <summary>A complete config exists but no probe has run this session. Hollow grey.</summary>
        NotTested,

        /// <summary>A probe is in flight. Blue.</summary>
        Testing,

        /// <summary>The last probe connected. Green.</summary>
        Connected,

        /// <summary>The last probe could not reach the server or the server errored. Red.</summary>
        Failed,
    }
}
