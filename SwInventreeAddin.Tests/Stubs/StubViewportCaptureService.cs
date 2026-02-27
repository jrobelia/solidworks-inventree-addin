using System.Drawing;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.Tests.Stubs
{
    public class StubViewportCaptureService : IViewportCaptureService
    {
        public Image ImageToReturn { get; set; }

        public StubViewportCaptureService()
        {
            // Default: 100x100 white bitmap.
            var bmp = new Bitmap(100, 100);
            using (var g = Graphics.FromImage(bmp))
                g.Clear(Color.White);
            ImageToReturn = bmp;
        }

        public Image CaptureViewportImage() => ImageToReturn;
    }
}