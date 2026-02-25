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

            var root = document.RootElement;
            if (!root.TryGetProperty(_serverName, out var serverElement))
                throw new InvalidOperationException(
                    $"Server '{_serverName}' not found in configuration file.");

            return new ServerConfig
            {
                Url    = serverElement.GetProperty("Url").GetString()    ?? string.Empty,
                ApiKey = serverElement.GetProperty("ApiKey").GetString() ?? string.Empty
            };
        }
    }
}
