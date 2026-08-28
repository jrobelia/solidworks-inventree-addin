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

        public PropertyMappingConfig? LastSaved        { get; private set; }
        public bool                   CopyToLocalCalled { get; private set; }

        public System.Exception? ThrowOnGet  { get; set; }
        public System.Exception? ThrowOnSave { get; set; }
        public System.Exception? ThrowOnCopyToLocal { get; set; }

        public PropertyMappingConfig GetMapping()
        {
            if (ThrowOnGet != null)
                throw ThrowOnGet;

            return Config;
        }

        public void SaveMapping(PropertyMappingConfig config)
        {
            if (ThrowOnSave != null)
                throw ThrowOnSave;

            LastSaved = config;
            Config    = config;
        }

        public void CopyToLocal()
        {
            if (ThrowOnCopyToLocal != null)
                throw ThrowOnCopyToLocal;

            CopyToLocalCalled = true;
            IsReadOnly        = false;
        }
    }
}
