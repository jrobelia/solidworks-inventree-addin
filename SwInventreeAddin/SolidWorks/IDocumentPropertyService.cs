namespace SwInventreeAddin.SolidWorks
{
    public interface IDocumentPropertyService
    {
        string GetCustomProperty(string name);
        void SetCustomProperty(string name, string value);
    }
}
