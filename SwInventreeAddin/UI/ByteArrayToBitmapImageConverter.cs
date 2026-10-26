using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Converts a <c>byte[]</c> of raw image data into a <see cref="BitmapImage"/>
    /// that WPF Image controls can display.
    /// Returns <c>null</c> when the input is null or empty, which causes the XAML
    /// placeholder (no-image icon) to be shown instead.
    /// </summary>
    [ValueConversion(typeof(byte[]), typeof(BitmapImage))]
    public class ByteArrayToBitmapImageConverter : IValueConverter
    {
        /// <summary>
        /// Decode bound for the thumbnail pipeline — the Task Pane renders the
        /// image at 120px, so anything beyond this only spends memory. Setting
        /// DecodePixelWidth also makes WPF skip decoding the full-resolution
        /// frame of a hostile oversized image.
        /// </summary>
        internal const int MaxDecodePixelWidth = 256;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is byte[] bytes) || bytes.Length == 0)
                return null;

            try
            {
                var bitmap = new BitmapImage();
                using (var stream = new MemoryStream(bytes))
                {
                    // Read frame metadata without decoding pixels — DecodePixelWidth
                    // upscales images smaller than the cap, so it is applied only
                    // when the source actually exceeds it.
                    var probe = BitmapDecoder.Create(
                        new MemoryStream(bytes, writable: false),
                        BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // decode now; stream can be freed
                    bitmap.StreamSource = stream;
                    if (probe.Frames.Count > 0 && probe.Frames[0].PixelWidth > MaxDecodePixelWidth)
                        bitmap.DecodePixelWidth = MaxDecodePixelWidth;
                    bitmap.EndInit();
                }
                bitmap.Freeze(); // make thread-safe; also prevents further memory growth
                return bitmap;
            }
            catch
            {
                // Malformed or unsupported image data — return null so the placeholder shows.
                return null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
