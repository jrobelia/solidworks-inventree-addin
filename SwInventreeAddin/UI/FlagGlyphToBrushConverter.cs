using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Converts a Segoe MDL2 Assets flag glyph string into a green (true) or red (false) brush.
    /// Returns <see cref="DependencyProperty.UnsetValue"/> when the input is null or empty,
    /// so the Foreground falls back to the parent text style.
    /// </summary>
    [ValueConversion(typeof(string), typeof(Brush))]
    public class FlagGlyphToBrushConverter : IValueConverter
    {
        private const string TrueGlyph  = "\uE73E"; // CheckMark
        private const string FalseGlyph = "\uE711"; // Cancel

        private static readonly SolidColorBrush TrueBrush  = CreateBrush(0x2E, 0x7D, 0x32);
        private static readonly SolidColorBrush FalseBrush = CreateBrush(0xC6, 0x28, 0x28);

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is string glyph))
                return DependencyProperty.UnsetValue;

            if (glyph == TrueGlyph)
                return TrueBrush;

            if (glyph == FalseGlyph)
                return FalseBrush;

            return DependencyProperty.UnsetValue;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
