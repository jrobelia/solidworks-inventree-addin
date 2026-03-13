namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Provides access to the property-name mapping configuration.
    /// Resolution order: source path (if configured) → local file → first-run defaults.
    /// </summary>
    public interface IPropertyMappingProvider
    {
        /// <summary>
        /// Returns the current mapping.
        /// Resolution: source path first (when configured and file exists), then local file,
        /// then first-run defaults (writes defaults to local path and returns them).
        /// </summary>
        PropertyMappingConfig GetMapping();

        /// <summary>Persists the mapping to the local file path.</summary>
        void SaveMapping(PropertyMappingConfig config);

        /// <summary>
        /// Copies the source-path config file to the local path, enabling local editing
        /// after the user clears the shared source path in Settings.
        /// Throws <see cref="System.InvalidOperationException"/> if no source path is configured
        /// or the source file does not exist.
        /// </summary>
        void CopyToLocal();

        /// <summary>
        /// True when a source path is configured and the source file exists — the UI
        /// shows the mapping as read-only and disables editing.
        /// False when no source path is configured (using local file or first-run defaults).
        /// </summary>
        bool IsReadOnly { get; }

        /// <summary>
        /// The absolute path to the local copy of the mapping file.
        /// Shown in the Settings window so the user can locate or copy the path.
        /// </summary>
        string LocalFilePath { get; }
    }
}
