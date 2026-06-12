using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ExchangeFileValidator.Controls;

/// <summary>Active filter -> accent funnel; inactive -> subtle grey.</summary>
public sealed class FunnelBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Active = New("#6D5DF5");   // indigo-violet accent
    private static readonly SolidColorBrush Idle = New("#80808080");   // translucent grey (reads on light+dark)
    public object Convert(object? value, Type t, object? p, CultureInfo c) => (value is bool b && b) ? Active : Idle;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    private static SolidColorBrush New(string h) { var br = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)!); br.Freeze(); return br; }
}

/// <summary>Shows "(Blanks)" for an empty distinct value so it is still tickable.</summary>
public sealed class EmptyToBlanksConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => string.IsNullOrEmpty(value as string) ? "(Blanks)" : value!;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
