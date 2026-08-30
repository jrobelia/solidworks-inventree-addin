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
        public PropertyMappingConfig Config       { get; set; } = new PropertyMappingConfig();
        public bool                  IsReadOnly   { get; set; } = false;
        public string                LocalFilePath { get; set; } = string.Empty;
        public MappingHealth         Health        { get; set; } = MappingHealth.Healthy;
        public string?               ErrorMessage   { get; set; }

        public PropertyMappingConfig? LastSaved        { get; private set; }
        public bool                   CopyToLocalCalled { get; private set; }

        public System.Exception? ThrowOnGet  { get; set; }
        public System.Exception? ThrowOnSave { get; set; }
        public System.Exception? ThrowOnCopyToLocal { get; set; }

        public event EventHandler? MappingChanged;

        public MappingResult GetMappingResult()
        {
            if (ThrowOnGet != null)
                throw ThrowOnGet;

            return new MappingResult(Health, Config, ErrorMessage);
        }

        public PropertyMappingConfig GetMapping()
        {
            var result = GetMappingResult();

            if (result.Health == MappingHealth.Invalid)
                throw new InvalidOperationException(
                    result.ErrorMessage ?? "The mapping configuration is invalid.");

            return result.Config;
        }

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
