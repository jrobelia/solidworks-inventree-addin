using System;

namespace SwInventreeAddin.Config
{
    public class JsonFileConfigProvider : IConfigProvider
    {
        private readonly string _filePath;
        private readonly string _serverName;

        public JsonFileConfigProvider(string filePath, string serverName = "staging")
        {
            _filePath = filePath;
            _serverName = serverName;
        }

        public ServerConfig GetServerConfig()
        {
            throw new NotImplementedException();
        }
    }
}
