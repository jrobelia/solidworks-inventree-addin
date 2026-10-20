namespace SwInventreeAddin.UI
{
    /// <summary>
    /// The Settings window's Save outcome as a fact — the apply's success flag
    /// plus the diagnostic the footer reported. The dialog captures it when
    /// Save runs so the host can carry it to the Task Pane after the dialog
    /// closes; a failed Save keeps the dialog open, so the fact still travels
    /// on a later close or cancel. <see cref="Diagnostic"/> is the footer's
    /// already-safe wording — it never contains a credential.
    /// </summary>
    public class SettingsSaveOutcome
    {
        /// <summary>Creates the fact from the apply's result and footer line.</summary>
        public SettingsSaveOutcome(bool success, string? diagnostic)
        {
            Success = success;
            Diagnostic = diagnostic;
        }

        /// <summary>True once the settings persisted and the mapping is usable.</summary>
        public bool Success { get; }

        /// <summary>The footer line the save reported, when there is one.</summary>
        public string? Diagnostic { get; }
    }
}
