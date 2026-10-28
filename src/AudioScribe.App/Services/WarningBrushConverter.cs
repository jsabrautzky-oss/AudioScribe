using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AudioScribe.App.Services;

/// <summary>true -> rote Warnfarbe, false -> grüne OK-Farbe (für Speicher-Balken).</summary>
public sealed class WarningBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool warn = value is bool flag && flag;
        var key = warn ? "SystemFillColorCriticalBrush" : "SystemFillColorSuccessBrush";
        return Application.Current?.TryFindResource(key) as Brush
               ?? (warn ? Brushes.Red : Brushes.Green);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
