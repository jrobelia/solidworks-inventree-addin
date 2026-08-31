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
        public bool                  IsReadOnly   { get; set; } = false;
        public string                LocalFilePath { get; set; } = string.Empty;
        public MappingHealth         Health        { get; set; } = MappingHealth.Healthy;
        public string?               Message        { get; set; }

        public PropertyMappingConfig? LastSaved        { get; private set; }
        public bool                   CopyToLocalCalled { get; private set; }

        public System.Exception? ThrowOnGet  { get; set; }
        public System.Exception? ThrowOnSave { get; set; }
        public System.Exception? ThrowOnCopyToLocal { get; set; }

        public event EventHandler? MappingChanged;

        /// <summary>Raises <see cref="MappingChanged"/> so tests can simulate an external save/copy.</summary>
        public void RaiseMappingChanged() => MappingChanged?.Invoke(this, EventArgs.Empty);

        public MappingResult GetMappingResult()
        {
            if (ThrowOnGet != null)
                throw ThrowOnGet;

            if (Health == MappingHealth.Invalid)
                return new MappingResult(MappingHealth.Invalid, Config, Message);

            return PropertyMappingProvider.Classify(Config, LocalFilePath);
        }

        public MappingResult ValidateMapping(PropertyMappingConfig config)
            => PropertyMappingProvider.Classify(config, LocalFilePath);

        public void SaveMapping(PropertyMappingConfig config)
        {
            if (ThrowOnSave != null)
                throw ThrowOnSave;

            LastSaved = config;
            Config    = config;
            MappingChanged?.Invoke(this, EventArgs.Empty);
        }

        public void CopyToLocal()
        {
            if (ThrowOnCopyToLocal != null)
                throw ThrowOnCopyToLocal;

            CopyToLocalCalled = true;
            IsReadOnly        = false;
            MappingChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
