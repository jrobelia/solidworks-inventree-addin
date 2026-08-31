using System;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Provides access to the property-name mapping configuration.
    /// Resolution order: source path (if configured and exists) →
    /// configured-and-missing source path (<see cref="MappingHealth.Invalid"/>; terminal) →
    /// local file (if exists) → first-run defaults.
    /// </summary>
    public interface IPropertyMappingProvider
    {
        /// <summary>
        /// Raised when <see cref="SaveMapping"/> or <see cref="CopyToLocal"/> changes the file,
        /// so shared consumers (Settings and Task Pane) can refresh from the same result.
        /// </summary>
        event EventHandler? MappingChanged;

        /// <summary>
        /// Returns the current mapping and its health.
        /// Resolution: source path first (when configured and file exists), then a configured
        /// but missing source path (<see cref="MappingHealth.Invalid"/>; terminal, no fallback),
        /// then local file, then first-run defaults (writes defaults to local path and returns them).
        /// Does not throw for corrupt or unreadable files; instead returns a
        /// <see cref="MappingResult"/> whose <see cref="MappingHealth"/> is <see cref="MappingHealth.Invalid"/>.
        /// </summary>
        MappingResult GetMappingResult();

        /// <summary>
        /// Returns a <see cref="MappingResult"/> for the supplied draft without
        /// persisting it, so the editor can validate a draft before saving.
        /// </summary>
        MappingResult ValidateMapping(PropertyMappingConfig config);

        /// <summary>
        /// Persists the mapping to the local file path.
        /// Throws <see cref="System.InvalidOperationException"/> if the file cannot be
        /// written or created, naming the offending path.
        /// </summary>
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
