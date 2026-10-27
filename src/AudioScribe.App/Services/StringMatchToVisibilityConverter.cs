using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioScribe.App.Services;

/// <summary>Sichtbarkeit, wenn zwei Strings gleich (Invert=false) bzw. ungleich (Invert=true) sind.</summary>
public sealed class StringMatchToVisibilityConverter : IMultiValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not { Length: 2 }) return Visibility.Collapsed;
        var a = values[0] as string ?? string.Empty;
        var b = values[1] as string ?? string.Empty;
        bool equal = string.Equals(a, b, StringComparison.Ordinal) && a.Length > 0;
        return (Invert ? !equal : equal) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
