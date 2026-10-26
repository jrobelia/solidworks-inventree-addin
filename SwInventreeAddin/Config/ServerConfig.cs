using System;

namespace SwInventreeAddin.Config
{
    public class ServerConfig
    {
        public string Url { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        /// <summary>
        /// Optional path to a shared property-mapping JSON file (e.g. a network share).
        /// Null means no source path is configured; the local %APPDATA% copy is used.
        /// Stored alongside credentials in the DPAPI-encrypted settings file.
        /// </summary>
        public string? MappingSourcePath { get; set; }

        /// <summary>
        /// Keyword used to identify the InvenTree BOM table in a SolidWorks assembly.
        /// Case-insensitive. Defaults to "inventree".
        /// </summary>
        public string BomKeyword { get; set; } = "inventree";

        /// <summary>
        /// The declared default of <see cref="WaitForServerAssignedIpn"/> — the single
        /// statement of the value, shared by the property initializer and every
        /// null-config fallback so they cannot drift apart (#259).
        /// </summary>
        public const bool DefaultWaitForServerAssignedIpn = true;

        /// <summary>
        /// When true, the Create Part flow polls InvenTree after creation, waiting
        /// for a server-assigned IPN. When false, the poll is skipped. Defaults to true
        /// so the Create Part dialog waits for a server-assigned IPN on first run.
        /// </summary>
        public bool WaitForServerAssignedIpn { get; set; } = DefaultWaitForServerAssignedIpn;

        /// <summary>
        /// True when a non-empty server URL is saved — the single "is the add-in
        /// configured" check for client construction. A saved record with an empty
        /// URL is legal (#253): the API key is kept, but there is no server to
        /// build a client against.
        /// </summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(Url);

        /// <summary>
        /// The https-scheme half of <see cref="IsAcceptedServerUrl"/>, kept
        /// separate so call sites that report the scheme failure with their
        /// own message can test it alone.
        /// </summary>
        internal static bool HasHttpsScheme(string url) =>
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The single server-URL acceptance rule — https scheme plus an
        /// absolute, parseable URI — shared by save-time validation and the
        /// load-time stored-URL re-check so the two cannot drift apart.
        /// </summary>
        internal static bool IsAcceptedServerUrl(string url) =>
            HasHttpsScheme(url) && Uri.TryCreate(url, UriKind.Absolute, out _);
    }
}
