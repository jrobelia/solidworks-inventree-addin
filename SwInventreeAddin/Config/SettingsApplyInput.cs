namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Values gathered from the <see cref="UI.SettingsWindow"/> that are persisted
    /// or validated by <see cref="ISettingsApplyService"/>.
    /// </summary>
    public class SettingsApplyInput
    {
        /// <summary>InvenTree server URL.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Username for token-based sign-in (optional).</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>Password for token-based sign-in (optional).</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>Raw API key, used when username and password are blank.</summary>
        public string RawApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Path to a shared mapping JSON file. When <c>null</c> or empty,
        /// the add-in falls back to the local mapping file.
        /// </summary>
        public string? SharedMappingPath { get; set; }

        /// <summary>Keyword used to locate the InvenTree BOM table in SolidWorks.</summary>
        public string BomKeyword { get; set; } = "inventree";

        /// <summary>Whether to wait for the server to assign an IPN on part creation.</summary>
        public bool WaitForAutoPartNumber { get; set; } = true;
    }
}
