namespace SwInventreeAddin.UI
{
    /// <summary>
    /// The Settings window's Save outcome as a fact — the footer's verdict
    /// (severity plus diagnostic line). The dialog captures it when Save runs
    /// so the host can carry it to the Task Pane after the dialog closes; the
    /// host forwards it only when the Save itself closed the dialog — a failed
    /// Save stays open, so its fact is abandoned on cancel.
    /// <see cref="Diagnostic"/> is the footer's already-safe wording — it
    /// never contains a credential.
    /// </summary>
    public class SettingsSaveOutcome
    {
        /// <summary>Creates the fact from the footer's severity and line.</summary>
        public SettingsSaveOutcome(StatusSeverity severity, string? diagnostic)
        {
            Severity = severity;
            Diagnostic = diagnostic;
        }

        /// <summary>The severity the footer reported the outcome at.</summary>
        public StatusSeverity Severity { get; }

        /// <summary>The footer line the save reported, when there is one.</summary>
        public string? Diagnostic { get; }
    }
}
