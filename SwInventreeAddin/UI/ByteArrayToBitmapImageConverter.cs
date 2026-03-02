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
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is byte[] bytes) || bytes.Length == 0)
                return null;

            try
            {
                var bitmap = new BitmapImage();
                using (var stream = new MemoryStream(bytes))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption  = BitmapCacheOption.OnLoad; // decode now; stream can be freed
                    bitmap.StreamSource = stream;
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
