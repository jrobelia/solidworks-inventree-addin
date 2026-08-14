using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Converts a nullable boolean to a Visibility: null collapses the target,
    /// while true or false makes it visible. Used for flag chips that should
    /// not appear until an InvenTree part has been loaded.
    /// </summary>
    [ValueConversion(typeof(bool?), typeof(Visibility))]
    public class NullableBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool ? Visibility.Visible : Visibility.Collapsed;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
