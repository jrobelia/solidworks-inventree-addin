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

        public DocumentType GetDocumentType()
        {
            var modelDoc = _swApp.ActiveDoc as IModelDoc2;
            if (modelDoc == null) return DocumentType.Unknown;
            return (swDocumentTypes_e)modelDoc.GetType() switch
            {
                swDocumentTypes_e.swDocPART     => DocumentType.Part,
                swDocumentTypes_e.swDocASSEMBLY => DocumentType.Assembly,
                swDocumentTypes_e.swDocDRAWING  => DocumentType.Drawing,
                _                               => DocumentType.Unknown,
            };
        }

        public string GetCustomProperty(string name)
        {
            // Use 'as' rather than a direct cast: when no document is open,
            // the COM property returns null and a hard cast throws InvalidCastException.
            var modelDoc = _swApp.ActiveDoc as IModelDoc2;
            if (modelDoc == null)
                return string.Empty;

            var mgr = modelDoc.Extension.CustomPropertyManager[""];
            mgr.Get4(name, false, out _, out string resolved);
            return resolved ?? string.Empty;
        }

        public void SetCustomProperty(string name, string value)
        {
            var modelDoc = _swApp.ActiveDoc as IModelDoc2;
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

