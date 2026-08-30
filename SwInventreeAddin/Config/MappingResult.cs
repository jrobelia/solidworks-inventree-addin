using System;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// The result of evaluating a <see cref="PropertyMappingConfig"/>.
    /// Contains the loaded <see cref="PropertyMappingConfig"/> and the <see cref="MappingHealth"/>.
    /// </summary>
    public class MappingResult
    {
        /// <summary>
        /// Creates a new <see cref="MappingResult"/>.
        /// </summary>
        /// <param name="health">The health of the mapping.</param>
        /// <param name="config">The loaded or default mapping configuration. Must not be null.</param>
        /// <param name="message">An optional human-readable message for the current <paramref name="health"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
        public MappingResult(MappingHealth health, PropertyMappingConfig config, string? message = null)
        {
            Health = health;
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Message = message;
        }

        /// <summary>The evaluated health of the mapping file.</summary>
        public MappingHealth Health { get; }

        /// <summary>The mapping configuration. Never null; may be the default configuration when <see cref="Health"/> is <see cref="MappingHealth.Invalid"/>.</summary>
        public PropertyMappingConfig Config { get; }

        /// <summary>Human-readable message that explains the current <see cref="Health"/>.</summary>
        public string? Message { get; }

        /// <summary>True when Apply, Push, Create Part, and BOM Compare are allowed.</summary>
        public bool CanUseForPartSync => Health == MappingHealth.Healthy;

        /// <summary>True when Fetch is allowed. Fetch is allowed unless the mapping is Invalid (Healthy, NeedsUpgrade, and NewerSchema all allow read-only inspection).</summary>
        public bool CanFetch => Health != MappingHealth.Invalid;

        /// <summary>True when the mapping editor may be opened. Invalid mappings open read-only.</summary>
        public bool CanEdit => Health != MappingHealth.Invalid;
    }
}
