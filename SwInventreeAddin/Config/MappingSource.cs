namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Identifies where the active <see cref="PropertyMappingConfig"/> was resolved from.
    /// </summary>
    public enum MappingSource
    {
        /// <summary>The local mapping file.</summary>
        Local,

        /// <summary>A shared mapping file configured in Settings.</summary>
        Shared,
    }
}
