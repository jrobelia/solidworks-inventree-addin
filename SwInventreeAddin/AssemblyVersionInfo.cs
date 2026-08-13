using System.Reflection;

namespace SwInventreeAddin
{
    /// <summary>
    /// Returns the version of the assembly containing the add-in.
    /// </summary>
    public class AssemblyVersionInfo : IVersionInfo
    {
        /// <inheritdoc />
        public string Version =>
            typeof(AssemblyVersionInfo).Assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
