namespace SwInventreeAddin.Config
{
    public interface IConfigProvider
    {
        ServerConfig? GetServerConfig();
        void SaveServerConfig(ServerConfig config);

        /// <summary>
        /// Deletes the saved server settings. A no-op when nothing is saved.
        /// Post-condition: <see cref="GetServerConfig"/> returns <c>null</c>.
        /// Throws <see cref="System.InvalidOperationException"/> with a
        /// caller-readable message when the settings file exists but cannot
        /// be deleted.
        /// </summary>
        void DeleteServerConfig();
    }
}
