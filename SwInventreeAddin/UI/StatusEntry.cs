namespace SwInventreeAddin.UI
{
    public enum StatusPersistence
    {
        Transient,
        Persistent,
    }

    public sealed class StatusEntry
    {
        public StatusEntry(string text, StatusSeverity severity, StatusPersistence persistence, string domain,
            string? detail = null)
        {
            Text = text;
            Severity = severity;
            Persistence = persistence;
            Domain = domain;
            Detail = detail;
        }

        public string Text { get; }
        public StatusSeverity Severity { get; }
        public StatusPersistence Persistence { get; }
        public string Domain { get; }
        public string? Detail { get; }
    }
}
