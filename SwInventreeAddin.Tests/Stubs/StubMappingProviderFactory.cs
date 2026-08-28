using System;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests.Stubs
{
    public class StubMappingProviderFactory : IMappingProviderFactory
    {
        public Func<string?, IPropertyMappingProvider>? Factory { get; set; }

        public IPropertyMappingProvider Create(string? sharedPath)
            => Factory?.Invoke(sharedPath) ?? new StubPropertyMappingProvider();
    }
}
