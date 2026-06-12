using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Views;

/// <summary>Maps an IsrLevel to a chip background colour.</summary>
public sealed class LevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush L0 = New("#2E7D32"); // green
    private static readonly SolidColorBrush L1 = New("#1565C0"); // blue
    private static readonly SolidColorBrush L2 = New("#6D5DF5"); // indigo-violet (accent)
    private static readonly SolidColorBrush L21 = New("#8E24AA"); // purple
    private static readonly SolidColorBrush L3 = New("#C62828"); // red
    private static readonly SolidColorBrush Un = New("#555555"); // grey

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        IsrLevel.Level0 => L0,
        IsrLevel.Level1 => L1,
        IsrLevel.Level2 => L2,
        IsrLevel.Level2_1 => L21,
        IsrLevel.Level3 => L3,
        _ => Un
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
