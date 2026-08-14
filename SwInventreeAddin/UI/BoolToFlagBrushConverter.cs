using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Converts a nullable bool to the brush used for a flag chip glyph:
    /// green (<see cref="Brushes.Green"/>) for true, red (<see cref="Brushes.Red"/>)
    /// for false, and transparent for null (no part loaded).
    /// </summary>
    [ValueConversion(typeof(bool?), typeof(Brush))]
    public class BoolToFlagBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is bool b))
                return Brushes.Transparent;
            return b ? Brushes.Green : Brushes.Red;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
