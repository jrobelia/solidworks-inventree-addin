namespace SwInventreeAddin.Config
{
    /// <summary>
    /// What the engineer is told about the saved InvenTree server settings, derived
    /// from a <see cref="ServerConfig"/>. Never exposes the saved API key (ADR-0022).
    /// </summary>
    public class ServerConnectionStatus
    {
        private ServerConnectionStatus(string message, string serverUrl)
        {
            Message = message;
            ServerUrl = serverUrl;
        }

        /// <summary>Human-readable summary of what is saved, e.g. "Server connection configured — API key saved".</summary>
        public string Message { get; }

        /// <summary>The saved server URL, trimmed. Empty when no server settings are saved.</summary>
        public string ServerUrl { get; }

        /// <summary>True when a server URL is saved and worth showing.</summary>
        public bool HasServerUrl => ServerUrl.Length > 0;

        /// <summary>
        /// Evaluates the supplied server configuration. A null config, or one with a
        /// blank URL, counts as nothing saved.
        /// </summary>
        public static ServerConnectionStatus From(ServerConfig? config)
        {
            string url = (config?.Url ?? string.Empty).Trim();

            if (url.Length == 0)
                return new ServerConnectionStatus(NoSettingsMessage, string.Empty);

            bool hasApiKey = !string.IsNullOrWhiteSpace(config?.ApiKey);

            return new ServerConnectionStatus(
                hasApiKey ? ConfiguredWithApiKeyMessage : ConfiguredWithoutApiKeyMessage,
                url);
        }

        /// <summary>Shown when no server settings have been saved yet.</summary>
        public const string NoSettingsMessage = "No server settings saved";

        /// <summary>Shown when a server URL and an API key are both saved.</summary>
        public const string ConfiguredWithApiKeyMessage = "Server connection configured \u2014 API key saved";

        /// <summary>Shown when a server URL is saved but no API key is stored with it.</summary>
        public const string ConfiguredWithoutApiKeyMessage = "Server connection configured \u2014 no API key saved";
    }
}
