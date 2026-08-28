namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Creates <see cref="PropertyMappingProvider"/> instances for the configured shared path.
    /// </summary>
    public class PropertyMappingProviderFactory : IMappingProviderFactory
    {
        /// <inheritdoc/>
        public IPropertyMappingProvider Create(string? sharedPath)
            => new PropertyMappingProvider(sharedPath);
    }
}
