using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests.Stubs
{
    public class StubConfigProvider : IConfigProvider
    {
        private readonly ServerConfig _config;

        public ServerConfig? LastSavedConfig { get; private set; }
        public System.Exception? ThrowOnSave { get; set; }

        /// <summary>The config returned by GetServerConfig — mutable so tests can change saved values.</summary>
        public ServerConfig Config => _config;

        public StubConfigProvider(string url = "http://stub.example.com", string apiKey = "stub-key")
        {
            _config = new ServerConfig { Url = url, ApiKey = apiKey };
        }

        public ServerConfig GetServerConfig() => _config;

        public void SaveServerConfig(ServerConfig config)
        {
            if (ThrowOnSave != null)
                throw ThrowOnSave;

            LastSavedConfig = config;
        }
    }
}
