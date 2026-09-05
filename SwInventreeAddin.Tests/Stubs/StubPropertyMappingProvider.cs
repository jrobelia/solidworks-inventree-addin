using System;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests.Stubs
{
    /// <summary>
    /// In-memory stub for tests that need an IPropertyMappingProvider
    /// without touching the file system.
    /// </summary>
    public class StubPropertyMappingProvider : IPropertyMappingProvider
    {
        public PropertyMappingConfig Config       { get; set; } = PropertyMappingConfig.WithDefaults();
        public string                LocalFilePath { get; set; } = string.Empty;
        public string?               SourceFilePath { get; set; }
        public bool                  SourceFileExists { get; set; } = true;
        public MappingHealth         Health        { get; set; } = MappingHealth.Healthy;
        public string?               Message        { get; set; }

        public PropertyMappingConfig? LastSaved        { get; private set; }

        public System.Exception? ThrowOnGet  { get; set; }
        public System.Exception? ThrowOnSave { get; set; }

        public event EventHandler? MappingChanged;

        /// <summary>Raises <see cref="MappingChanged"/> so tests can simulate an external save.</summary>
        public void RaiseMappingChanged() => MappingChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>The file the provider resolves to, mirroring production: source when it exists, otherwise local.</summary>
        public string ResolvedFilePath =>
            SourceFileExists && !string.IsNullOrEmpty(SourceFilePath)
                ? SourceFilePath!
                : LocalFilePath;

        /// <summary>The mapping source this provider resolves to, mirroring production.</summary>
        public MappingSource Source =>
            SourceFileExists && !string.IsNullOrEmpty(SourceFilePath)
                ? MappingSource.Shared
                : MappingSource.Local;

        public MappingResult GetMappingResult()
        {
            if (ThrowOnGet != null)
                throw ThrowOnGet;

            if (Health == MappingHealth.Invalid)
                return new MappingResult(MappingHealth.Invalid, Config, Message, ResolvedFilePath, Source);

            return PropertyMappingProvider.Classify(Config, ResolvedFilePath, Source);
        }

        public MappingResult ValidateMapping(PropertyMappingConfig config)
            => PropertyMappingProvider.Classify(config, ResolvedFilePath, Source);

        public void SaveMapping(PropertyMappingConfig config)
        {
            if (ThrowOnSave != null)
                throw ThrowOnSave;

            LastSaved = config;
            Config    = config;
            MappingChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
