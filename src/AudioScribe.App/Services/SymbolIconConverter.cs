using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace AudioScribe.App.Services;

/// <summary>Parameter (Symbol-Regular-Name, z. B. "PlayCircle24") -> SymbolIcon-Instanz.</summary>
public sealed class SymbolIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = parameter as string ?? value as string ?? string.Empty;
        if (Enum.TryParse<SymbolRegular>(name, out var sym))
            return new SymbolIcon { Symbol = sym, FontSize = 18 };
        return new SymbolIcon { Symbol = SymbolRegular.Question24, FontSize = 18 };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
