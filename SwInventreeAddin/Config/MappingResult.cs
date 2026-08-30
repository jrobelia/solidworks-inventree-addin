using System;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// The result of evaluating a <see cref="PropertyMapping"/> file.
    /// Contains the loaded <see cref="PropertyMappingConfig"/> and the <see cref="MappingHealth"/>.
    /// </summary>
    public class MappingResult
    {
        /// <summary>
        /// Creates a new <see cref="MappingResult"/>.
        /// </summary>
        /// <param name="health">The health of the mapping.</param>
        /// <param name="config">The loaded or default mapping configuration. Must not be null.</param>
        /// <param name="errorMessage">An optional human-readable message when <paramref name="health"/> is <see cref="MappingHealth.Invalid"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
        public MappingResult(MappingHealth health, PropertyMappingConfig config, string? errorMessage = null)
        {
            Health = health;
            Config = config ?? throw new ArgumentNullException(nameof(config));
            ErrorMessage = errorMessage;
        }

        /// <summary>The evaluated health of the mapping file.</summary>
        public MappingHealth Health { get; }

        /// <summary>The mapping configuration. Never null; may be the default configuration when <see cref="Health"/> is <see cref="MappingHealth.Invalid"/>.</summary>
        public PropertyMappingConfig Config { get; }

        /// <summary>Human-readable error message when <see cref="Health"/> is <see cref="MappingHealth.Invalid"/>.</summary>
        public string? ErrorMessage { get; }

        /// <summary>True when Apply, Push, Create Part, and BOM Compare are allowed.</summary>
        public bool CanUseForPartSync => Health == MappingHealth.Healthy;

        /// <summary>True when Fetch is allowed. Fetch is allowed for Healthy and NeedsUpgrade mappings.</summary>
        public bool CanFetch => Health == MappingHealth.Healthy || Health == MappingHealth.NeedsUpgrade;

        /// <summary>True when the mapping editor may be opened. Invalid mappings open read-only.</summary>
        public bool CanEdit => Health != MappingHealth.Invalid;
    }
}
