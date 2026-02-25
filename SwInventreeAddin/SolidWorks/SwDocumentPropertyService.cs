using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SwInventreeAddin.SolidWorks
{
    /// <summary>
    /// Reads and writes SolidWorks custom document properties.
    /// Requires a live SolidWorks session — not exercised by automated tests.
    /// </summary>
    public class SwDocumentPropertyService : IDocumentPropertyService
    {
        private readonly ISldWorks _swApp;

        public SwDocumentPropertyService(ISldWorks swApp)
        {
            _swApp = swApp;
        }

        public string GetCustomProperty(string name)
        {
            var modelDoc = (IModelDoc2)_swApp.ActiveDoc;
            if (modelDoc == null)
                return string.Empty;

            var mgr = modelDoc.Extension.CustomPropertyManager[""];
            mgr.Get4(name, false, out _, out string resolved);
            return resolved ?? string.Empty;
        }

        public void SetCustomProperty(string name, string value)
        {
            var modelDoc = (IModelDoc2)_swApp.ActiveDoc;
            if (modelDoc == null)
                return;

            var mgr = modelDoc.Extension.CustomPropertyManager[""];
            mgr.Add3(
                name,
                (int)swCustomInfoType_e.swCustomInfoText,
                value,
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
        }
    }
}

