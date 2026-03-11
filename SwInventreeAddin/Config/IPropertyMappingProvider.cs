namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Provides access to the property-name mapping configuration.
    /// Resolution order: local file → source path (if configured) → first-run defaults.
    /// </summary>
    public interface IPropertyMappingProvider
    {
        /// <summary>
        /// Returns the current mapping.
        /// On first run (no local file, no source path) writes defaults to the local path and returns them.
        /// </summary>
        PropertyMappingConfig GetMapping();

        /// <summary>Persists the mapping to the local file path.</summary>
        void SaveMapping(PropertyMappingConfig config);

        /// <summary>
        /// Copies the source-path config file to the local path, enabling local editing.
        /// Throws <see cref="System.InvalidOperationException"/> if no source path is configured
        /// or the source file does not exist.
        /// </summary>
        void CopyToLocal();

        /// <summary>
        /// True when the mapping is being loaded from the configured source path
        /// (no local override exists). The UI shows the mapping as read-only in this state.
        /// False when a local file is present or no source path is configured.
        /// </summary>
        bool IsReadOnly { get; }
    }
}
