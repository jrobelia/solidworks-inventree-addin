using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests.Stubs
{
    public class StubConfigProvider : IConfigProvider
    {
        private readonly ServerConfig _config;

        public StubConfigProvider(string url = "http://stub.example.com", string apiKey = "stub-key")
        {
            _config = new ServerConfig { Url = url, ApiKey = apiKey };
        }

        public ServerConfig GetServerConfig() => _config;
    }
}
