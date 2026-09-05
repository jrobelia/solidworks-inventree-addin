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
        /// Raised when the mapping file changes — by <see cref="SaveMapping"/>, or by an
        /// external edit detected when <see cref="GetMappingResult"/> reads a different
        /// <see cref="MappingHealth"/> than the previous read. Shared consumers
        /// (Settings and Task Pane) refresh from the same result.
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
        /// Persists the mapping to the resolved file path:
        /// the shared source path when it is configured and the file exists, otherwise the local path.
        /// Throws <see cref="System.InvalidOperationException"/> if the file cannot be
        /// written or created, naming the offending path.
        /// </summary>
        void SaveMapping(PropertyMappingConfig config);

        /// <summary>
        /// The absolute path to the local copy of the mapping file.
        /// Shown in the Settings window so the user can locate or copy the path.
        /// </summary>
        string LocalFilePath { get; }
    }
}
