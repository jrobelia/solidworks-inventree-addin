namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Classifies the source of a failure during the Settings save/apply flow.
    /// </summary>
    public enum SettingsApplyErrorKind
    {
        /// <summary>
        /// A server-config or credential persistence failure.
        /// Maps to a "Failed to save server settings" status message in the UI.
        /// </summary>
        Config,

        /// <summary>
        /// A mapping-file failure.
        /// Maps to a "Failed to load mapping file" status message in the UI.
        /// </summary>
        Mapping,
    }
}
