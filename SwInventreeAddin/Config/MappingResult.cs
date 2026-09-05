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
        /// <param name="resolvedFilePath">The absolute path of the file the mapping was resolved from, if any.</param>
        /// <param name="source">The source of the resolved mapping file.</param>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
        public MappingResult(MappingHealth health, PropertyMappingConfig config, string? message = null, string? resolvedFilePath = null, MappingSource source = MappingSource.Local)
        {
            Health = health;
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Message = message;
            ResolvedFilePath = resolvedFilePath;
            Source = source;
        }

        /// <summary>The evaluated health of the mapping file.</summary>
        public MappingHealth Health { get; }

        /// <summary>The mapping configuration. Never null; may be the default configuration when <see cref="Health"/> is <see cref="MappingHealth.Invalid"/>.</summary>
        public PropertyMappingConfig Config { get; }

        /// <summary>Human-readable message that explains the current <see cref="Health"/>.</summary>
        public string? Message { get; }

        /// <summary>
        /// The full status text shown in a status bar, combining the health state
        /// with the actionable detail. The tooltip for the same status bar carries
        /// the same text so the full message is readable and copyable.
        /// </summary>
        public string FullStatusMessage
        {
            get
            {
                var tooltip = ToolTip ?? string.Empty;
                return Health switch
                {
                    MappingHealth.Healthy      => MessageOrDefault,
                    MappingHealth.NeedsUpgrade => $"{EnsureTrailingPunctuation(MessageOrDefault)} {tooltip}",
                    MappingHealth.NewerSchema  => $"{EnsureTrailingPunctuation(MessageOrDefault)} {tooltip}",
                    MappingHealth.Invalid      => string.IsNullOrEmpty(Message)
                        ? tooltip
                        : $"{GetDefaultMessage(Health)} {EnsureTrailingPunctuation(Message!)} {InvalidMappingHelp}",
                    _                          => MessageOrDefault,
                };
            }
        }

        private static string EnsureTrailingPunctuation(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var trimmed = text.TrimEnd();
            return char.IsPunctuation(trimmed[trimmed.Length - 1]) ? trimmed : trimmed + ".";
        }

        /// <summary>
        /// The absolute path of the file the mapping was resolved from.
        /// This is the file <see cref="IPropertyMappingProvider.SaveMapping"/> will write to.
        /// </summary>
        public string? ResolvedFilePath { get; }

        /// <summary>The source of the resolved mapping file (local or shared).</summary>
        public MappingSource Source { get; }

        /// <summary>
        /// Human-readable message for the current <see cref="Health"/>.
        /// Falls back to a default label when <see cref="Message"/> is <c>null</c>.
        /// </summary>
        public string MessageOrDefault => Message ?? GetDefaultMessage(Health);

        /// <summary>True when Apply, Push, Create Part, and BOM Compare are allowed.</summary>
        public bool CanUseForPartSync => Health == MappingHealth.Healthy;

        /// <summary>True when Fetch is allowed. Only a Healthy mapping supports read-only inspection.</summary>
        public bool CanFetch => Health == MappingHealth.Healthy;

        /// <summary>True when the mapping editor may be opened to edit and save the resolved file.</summary>
        public bool CanEdit => Health == MappingHealth.Healthy || Health == MappingHealth.NeedsUpgrade;

        /// <summary>
        /// Returns the default, source-independent human-readable label for the supplied <see cref="MappingHealth"/>.
        /// Used for the Settings and Task Pane status text; the <see cref="Message"/> property carries the detail for tooltips.
        /// </summary>
        public static string GetDefaultMessage(MappingHealth health) =>
            health switch
            {
                MappingHealth.Healthy     => "The Property Mapping file is up to date and valid.",
                MappingHealth.NeedsUpgrade => "The Property Mapping Schema is out of date.",
                MappingHealth.NewerSchema => "The Property Mapping Schema is newer than this add-in.",
                _                         => "The Property Mapping file is invalid.",
            };

        /// <summary>
        /// The actionable help text appended to <see cref="Invalid"/> messages.
        /// </summary>
        public const string InvalidMappingHelp = "Fix the file, replace it, or choose a different mapping source in Settings.";

        /// <summary>
        /// Source-independent tooltip for the mapping-health status.
        /// <see cref="Invalid"/> uses the caller-supplied <see cref="Message"/> detail, or the default message if none is supplied.
        /// </summary>
        public string? ToolTip =>
            Health switch
            {
                MappingHealth.Healthy      => null,
                MappingHealth.NeedsUpgrade => "Edit the Property Mapping and save to enable Part Sync.",
                MappingHealth.NewerSchema  => "Upgrade the add-in to enable Part Sync.",
                MappingHealth.Invalid      => $"{MessageOrDefault} {InvalidMappingHelp}",
                _                          => null,
            };
    }
}
