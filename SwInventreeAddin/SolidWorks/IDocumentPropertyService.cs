namespace SwInventreeAddin.SolidWorks
{
    public interface IDocumentPropertyService
    {
        /// <summary>Returns the type of the currently active SolidWorks document.</summary>
        DocumentType GetDocumentType();
        string GetCustomProperty(string name);
        void SetCustomProperty(string name, string value);
        bool PropertyExists(string name);
    }
}
