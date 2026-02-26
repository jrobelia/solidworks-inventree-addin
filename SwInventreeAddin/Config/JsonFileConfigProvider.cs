using System;
using System.IO;
using System.Text.Json;

namespace SwInventreeAddin.Config
{
    public class JsonFileConfigProvider : IConfigProvider
    {
        private readonly string _filePath;
        private readonly string _serverName;

        public JsonFileConfigProvider(string filePath, string serverName = "staging")
        {
            _filePath   = filePath;
            _serverName = serverName;
        }

        /// <summary>
        /// JSON provider is read-only — saving is handled by EncryptedConfigProvider.
        /// This no-op satisfies the interface contract.
        /// </summary>
        public void SaveServerConfig(ServerConfig config) { }

        public ServerConfig GetServerConfig()
        {
            if (!File.Exists(_filePath))
                throw new FileNotFoundException(
                    $"Configuration file not found at: {_filePath}", _filePath);

            string json;
            try
            {
                json = File.ReadAllText(_filePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not read configuration file at {_filePath}: {ex.Message}", ex);
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new Exception(
                    $"inventree_servers.json is not valid JSON: {ex.Message}", ex);
            }

            using (document)
            {
                var root = document.RootElement;
                if (!root.TryGetProperty("servers", out var serversElement))
                    throw new InvalidOperationException(
                        "inventree_servers.json is missing the 'servers' section.");

                if (!serversElement.TryGetProperty(_serverName, out var serverElement))
                    throw new InvalidOperationException(
                        $"Server '{_serverName}' not found in inventree_servers.json.");

                if (!serverElement.TryGetProperty("url", out var urlElement) ||
                    !serverElement.TryGetProperty("api_key", out var apiKeyElement))
                    throw new InvalidOperationException(
                        $"Server '{_serverName}' in inventree_servers.json is missing 'url' or 'api_key'.");

                return new ServerConfig
                {
                    Url    = urlElement.GetString()    ?? string.Empty,
                    ApiKey = apiKeyElement.GetString() ?? string.Empty
                };
            }
        }
    }
}
