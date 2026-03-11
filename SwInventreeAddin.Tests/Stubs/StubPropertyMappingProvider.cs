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

        public PropertyMappingConfig? LastSaved        { get; private set; }
        public bool                   CopyToLocalCalled { get; private set; }

        public PropertyMappingConfig GetMapping() => Config;

        public void SaveMapping(PropertyMappingConfig config)
        {
            LastSaved = config;
            Config    = config;
        }

        public void CopyToLocal()
        {
            CopyToLocalCalled = true;
            IsReadOnly        = false;
        }
    }
}
