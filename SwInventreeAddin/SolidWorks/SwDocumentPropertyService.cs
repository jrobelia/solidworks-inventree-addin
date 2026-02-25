namespace SwInventreeAddin.SolidWorks
{
    /// <summary>
    /// Reads and writes SolidWorks custom document properties.
    /// Requires a live SolidWorks session — not exercised by automated tests.
    /// </summary>
    public class SwDocumentPropertyService : IDocumentPropertyService
    {
        // SolidWorks application object is injected at runtime by SwAddin.
        // Stored as dynamic so the project compiles without a SolidWorks SDK reference.
        private readonly dynamic _swApp;

        public SwDocumentPropertyService(dynamic swApp)
        {
            _swApp = swApp;
        }

        public string GetCustomProperty(string name)
        {
            var modelDoc = _swApp.ActiveDoc;
            if (modelDoc == null)
                return string.Empty;

            var mgr        = modelDoc.Extension.CustomPropertyManager[""];
            string valOut  = string.Empty;
            string resolved = string.Empty;
            mgr.Get4(name, false, out valOut, out resolved);
            return resolved ?? string.Empty;
        }

        public void SetCustomProperty(string name, string value)
        {
            var modelDoc = _swApp.ActiveDoc;
            if (modelDoc == null)
                return;

            var mgr = modelDoc.Extension.CustomPropertyManager[""];
            // swCustomInfoText = 30, swCustomPropertyReplaceValue = 0
            mgr.Add3(name, 30, value, 0);
        }
    }
}
