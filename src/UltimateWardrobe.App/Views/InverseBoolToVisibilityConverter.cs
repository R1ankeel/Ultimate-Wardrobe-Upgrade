using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UltimateWardrobe.App.Views;

/// <summary>
/// Inverts a boolean into <see cref="Visibility"/>: true becomes <see cref="Visibility.Collapsed"/>,
/// false becomes <see cref="Visibility.Visible"/>. Used by the matrix to hide blank cells.
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
