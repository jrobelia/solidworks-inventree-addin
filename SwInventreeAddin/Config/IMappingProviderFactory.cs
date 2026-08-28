namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Creates a <see cref="IPropertyMappingProvider"/> for a given shared mapping path.
    /// </summary>
    public interface IMappingProviderFactory
    {
        /// <summary>
        /// Returns a mapping provider that uses <paramref name="sharedPath"/> when supplied,
        /// or the default local path when it is <c>null</c>.
        /// </summary>
        IPropertyMappingProvider Create(string? sharedPath);
    }
}
