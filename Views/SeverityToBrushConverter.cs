using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Views;

public sealed class SeverityToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Error = New("#C62828");
    private static readonly SolidColorBrush Warning = New("#EF8C00");
    private static readonly SolidColorBrush Info = New("#1565C0");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        Severity.Error => Error,
        Severity.Warning => Warning,
        _ => Info
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush New(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        return b;
    }
}
