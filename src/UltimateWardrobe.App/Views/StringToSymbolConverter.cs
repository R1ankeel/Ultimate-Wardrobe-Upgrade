using System.Globalization;
using System.Windows.Data;

namespace UltimateWardrobe.App.Views;

/// <summary>
/// Converts a WPF-UI <see cref="Wpf.Ui.Controls.SymbolRegular"/> member name string into the enum
/// value so <c>ui:SymbolIcon Symbol="{Binding Symbol}"</c> can be data-bound (Sprint 6.6 polish,
/// roadmap 8.5 status legend). Invalid names fall back to <c>Circle20</c> rather than throwing.
/// </summary>
public sealed class StringToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string symbol && Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(symbol, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return Wpf.Ui.Controls.SymbolRegular.Circle20;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
