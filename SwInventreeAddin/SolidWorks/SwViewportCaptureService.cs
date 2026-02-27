using System;
using System.Drawing;
using System.IO;
using SolidWorks.Interop.sldworks;

namespace SwInventreeAddin.SolidWorks
{
    /// <summary>
    /// Captures the SolidWorks 3D viewport as an image using the official
    /// <see cref="IModelDoc2.SaveBMP"/> API. This produces a clean viewport-only
    /// image with no menus, panels, or overlapping windows.
    /// </summary>
    public class SwViewportCaptureService : IViewportCaptureService
    {
        private readonly ISldWorks _swApp;

        public SwViewportCaptureService(ISldWorks swApp)
        {
            _swApp = swApp;
        }

        public Image CaptureViewportImage()
        {
            var modelDoc = _swApp.ActiveDoc as IModelDoc2;
            if (modelDoc == null)
                throw new InvalidOperationException("No document is open in SolidWorks.");

            // Save the viewport to a temporary BMP file.
            // SaveBMP renders at the specified dimensions regardless of window size.
            var tempPath = Path.Combine(Path.GetTempPath(), $"sw_capture_{Guid.NewGuid():N}.bmp");

            try
            {
                bool ok = modelDoc.SaveBMP(tempPath, 0, 0);
                if (!ok || !File.Exists(tempPath))
                    throw new InvalidOperationException("SolidWorks SaveBMP failed — the viewport image could not be captured.");

                // Load into memory and detach from the file so we can delete it.
                Image image;
                using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read))
                {
                    image = Image.FromStream(fs);
                }

                return image;
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* best effort cleanup */ }
            }
        }
    }
}
