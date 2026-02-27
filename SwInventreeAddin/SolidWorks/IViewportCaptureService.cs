using System.Drawing;

namespace SwInventreeAddin.SolidWorks
{
    /// <summary>
    /// Captures the active SolidWorks viewport as a <see cref="System.Drawing.Image"/>.
    /// Tests use a stub; the real implementation uses the SolidWorks API.
    /// </summary>
    public interface IViewportCaptureService
    {
        Image CaptureViewportImage();
    }
}
