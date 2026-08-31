using System.Text.Json.Serialization;

namespace SwInventreeAddin.Config
{
    public class ServerConfig
    {
        public string  Url               { get; set; } = string.Empty;
        public string  ApiKey            { get; set; } = string.Empty;
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
        /// When true, the Create Part flow polls InvenTree after creation, waiting
        /// for a server-assigned IPN. When false, the poll is skipped. Defaults to true
        /// so the Create Part dialog waits for a server-assigned IPN on first run.
        /// </summary>
        public bool WaitForServerAssignedIpn { get; set; } = true;

        /// <summary>
        /// Backward-compatibility alias for <see cref="WaitForServerAssignedIpn"/>.
        /// Reading or writing this property affects <see cref="WaitForServerAssignedIpn"/>.
        /// It is not serialised; legacy values are migrated on load.
        /// </summary>
        [JsonIgnore]
        public bool WaitForAutoPartNumber
        {
            get => WaitForServerAssignedIpn;
            set => WaitForServerAssignedIpn = value;
        }
    }
}
