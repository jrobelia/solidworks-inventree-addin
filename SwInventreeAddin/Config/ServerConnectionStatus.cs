using System;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Everything the Settings status card renders, derived from two axes: what is
    /// saved (<see cref="ServerConfig"/>) and the last live probe
    /// (<see cref="ConnectionProbeResult"/>, session-only). No produced string ever
    /// contains the API key (ADR-0022). Wording matches prototype 1b.
    /// </summary>
    public class ServerConnectionStatus : IEquatable<ServerConnectionStatus>
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

            // A NotProbed result is a no-verdict placeholder (#249 — the save
            // skipped the probe): the card renders it exactly like "no probe
            // verdict yet", never as a failure.
            if (lastProbe == null || lastProbe.Status == ConnectionProbeStatus.NotProbed)
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

        /// <summary>
        /// Value equality over every projected member — consumers gate
        /// change-notification on a real state move, so a field added later
        /// joins the comparison here rather than silently never registering.
        /// </summary>
        public bool Equals(ServerConnectionStatus? other) =>
            other != null &&
            IsSaved == other.IsSaved &&
            IsComplete == other.IsComplete &&
            Indicator == other.Indicator &&
            string.Equals(Title, other.Title, StringComparison.Ordinal) &&
            string.Equals(ServerLine, other.ServerLine, StringComparison.Ordinal) &&
            string.Equals(CredentialLine, other.CredentialLine, StringComparison.Ordinal) &&
            string.Equals(ConnectionLine, other.ConnectionLine, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as ServerConnectionStatus);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + IsSaved.GetHashCode();
                hash = (hash * 31) + IsComplete.GetHashCode();
                hash = (hash * 31) + Indicator.GetHashCode();
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Title);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ServerLine);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(CredentialLine);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ConnectionLine);
                return hash;
            }
        }
    }
}
