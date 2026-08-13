namespace SwInventreeAddin
{
    /// <summary>
    /// Provides a human-readable version/build identifier for the running add-in assembly.
    /// </summary>
    public interface IVersionInfo
    {
        /// <summary>
        /// A version or build string identifying the running add-in.
        /// </summary>
        string Version { get; }
    }
}
