namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Everything the Settings status card renders, derived from two axes: what is
    /// saved (<see cref="ServerConfig"/>) and the last live probe
    /// (<see cref="ConnectionProbeResult"/>, session-only). No produced string ever
    /// contains the API key (ADR-0022). Wording matches prototype 1b.
    /// </summary>
    public class ServerConnectionStatus
    {
        private ServerConnectionStatus(
            bool isSaved,
            bool isComplete,
            ServerConnectionIndicator indicator,
            string title,
            string serverLine,
            string credentialLine,
            string connectionLine)
        {
            IsSaved = isSaved;
            IsComplete = isComplete;
            Indicator = indicator;
            Title = title;
            ServerLine = serverLine;
            CredentialLine = credentialLine;
            ConnectionLine = connectionLine;
        }

        /// <summary>False for a null config or a blank URL — the card renders only when true.</summary>
        public bool IsSaved { get; }

        /// <summary>Saved URL and saved key — the card toolbar renders only when true.</summary>
        public bool IsComplete { get; }

        /// <summary>The dot state; brush lookup happens in XAML code-behind.</summary>
        public ServerConnectionIndicator Indicator { get; }

        /// <summary>Text title beside the dot, e.g. "Authentication required".</summary>
        public string Title { get; }

        /// <summary>Value behind the fixed "Server" label — the saved URL.</summary>
        public string ServerLine { get; }

        /// <summary>Value behind the fixed "Credential" label — presence only, never the key.</summary>
        public string CredentialLine { get; }

        /// <summary>Value behind the fixed "Connection" label — the last probe outcome.</summary>
        public string ConnectionLine { get; }

        /// <summary>
        /// Merges the saved config with the session's probe state. A missing key on a
        /// saved config always wins (the post-<see cref="ISettingsApplyService.RemoveApiKeyAsync"/>
        /// landing state); an in-flight probe outranks a stale <paramref name="lastProbe"/>.
        /// </summary>
        public static ServerConnectionStatus From(
            ServerConfig? config,
            ConnectionProbeResult? lastProbe = null,
            bool probeInFlight = false)
        {
            string url = (config?.Url ?? string.Empty).Trim();
            bool isSaved = config?.IsConfigured == true;
            bool hasKey = !string.IsNullOrWhiteSpace(config?.ApiKey);

            string serverLine = isSaved ? url : "not saved";
            string credentialLine = hasKey ? "API key saved" : "none saved";

            if (!isSaved)
            {
                return new ServerConnectionStatus(
                    isSaved: false, isComplete: false,
                    ServerConnectionIndicator.NotTested, "Not tested",
                    serverLine, credentialLine, "\u2014");
            }

            if (!hasKey)
            {
                return new ServerConnectionStatus(
                    isSaved: true, isComplete: false,
                    ServerConnectionIndicator.AuthenticationRequired, "Authentication required",
                    serverLine, credentialLine, "\u2014");
            }

            if (probeInFlight)
            {
                return new ServerConnectionStatus(
                    isSaved: true, isComplete: true,
                    ServerConnectionIndicator.Testing, "Testing connection\u2026",
                    serverLine, credentialLine, "testing\u2026");
            }

            if (lastProbe == null)
            {
                return new ServerConnectionStatus(
                    isSaved: true, isComplete: true,
                    ServerConnectionIndicator.NotTested, "Not tested",
                    serverLine, credentialLine, "not tested yet");
            }

            switch (lastProbe.Status)
            {
                case ConnectionProbeStatus.Connected:
                    return new ServerConnectionStatus(
                        isSaved: true, isComplete: true,
                        ServerConnectionIndicator.Connected, "Connected",
                        serverLine, credentialLine, "last test succeeded");

                case ConnectionProbeStatus.CredentialRejected:
                    return new ServerConnectionStatus(
                        isSaved: true, isComplete: true,
                        ServerConnectionIndicator.AuthenticationRequired, "Authentication required",
                        serverLine, credentialLine, lastProbe.Message);

                // "saved OK with no URL, nothing probed" — the unconfigured
                // state, identical to a blank saved URL, never a failure.
                case ConnectionProbeStatus.NotConfigured:
                    return new ServerConnectionStatus(
                        isSaved: false, isComplete: false,
                        ServerConnectionIndicator.NotTested, "Not tested",
                        "not saved", credentialLine, "\u2014");

                default:
                    return new ServerConnectionStatus(
                        isSaved: true, isComplete: true,
                        ServerConnectionIndicator.Failed, "Connection failed",
                        serverLine, credentialLine, lastProbe.Message);
            }
        }
    }
}
