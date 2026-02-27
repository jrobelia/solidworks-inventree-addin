using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Pure image processing: crop, resize to fit within 800x800, encode as PNG.
    /// No UI, no I/O beyond the returned byte array -- fully testable.
    /// </summary>
    public static class ImagePipeline
    {
        private const int MaxDimension = 800;

        /// <summary>
        /// Crops (if <paramref name="cropRect"/> is non-empty), resizes to fit
        /// within 800x800 preserving aspect ratio (never upscales), and encodes
        /// the result as a PNG byte array.
        /// </summary>
        public static byte[] Process(Image source, Rectangle cropRect)
        {
            Image working = source;
            bool ownWorking = false;

            try
            {
                // Step 1: crop if requested
                if (cropRect != Rectangle.Empty && cropRect.Width > 0 && cropRect.Height > 0)
                {
                    var cropped = new Bitmap(cropRect.Width, cropRect.Height);
                    using (var g = Graphics.FromImage(cropped))
                    {
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(source,
                            new Rectangle(0, 0, cropRect.Width, cropRect.Height),
                            cropRect,
                            GraphicsUnit.Pixel);
                    }
                    working = cropped;
                    ownWorking = true;
                }

                // Step 2: resize if either dimension exceeds MaxDimension (never upscale)
                int w = working.Width;
                int h = working.Height;

                if (w > MaxDimension || h > MaxDimension)
                {
                    double scale = System.Math.Min(
                        (double)MaxDimension / w,
                        (double)MaxDimension / h);

                    int newW = (int)(w * scale);
                    int newH = (int)(h * scale);

                    var resized = new Bitmap(newW, newH);
                    using (var g = Graphics.FromImage(resized))
                    {
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.DrawImage(working, 0, 0, newW, newH);
                    }

                    if (ownWorking) working.Dispose();
                    working = resized;
                    ownWorking = true;
                }

                // Step 3: encode as PNG
                using (var ms = new MemoryStream())
                {
                    working.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
            finally
            {
                if (ownWorking) working.Dispose();
            }
        }
    }
}
