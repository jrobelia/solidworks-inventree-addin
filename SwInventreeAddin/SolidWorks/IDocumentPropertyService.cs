namespace SwInventreeAddin.SolidWorks
{
    public interface IDocumentPropertyService
    {
        /// <summary>Returns the type of the currently active SolidWorks document.</summary>
        DocumentType GetDocumentType();

        /// <summary>
        /// An opaque identity for the currently active document — the file path
        /// when saved, the window title otherwise; null when no document is open.
        /// Callers compare tokens only (Ordinal): equal means "same document".
        /// </summary>
        string? GetActiveDocumentToken();
        string GetCustomProperty(string name);
        void SetCustomProperty(string name, string value);
        bool PropertyExists(string name);
    }
}
