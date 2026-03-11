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
    }
}
