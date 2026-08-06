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
        /// for a server-assigned part number. When false (default), the poll is skipped.
        /// </summary>
        public bool WaitForAutoPartNumber { get; set; } = false;
    }
}
