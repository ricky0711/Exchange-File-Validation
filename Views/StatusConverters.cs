using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Views;

/// <summary>Checklist status string -> chip colour.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Pass = New("#2E7D32");
    private static readonly SolidColorBrush Warn = New("#EF8C00");
    private static readonly SolidColorBrush Err = New("#C62828");
    private static readonly SolidColorBrush Manual = New("#616161");
    private static readonly SolidColorBrush NotRun = New("#9E9E9E");

    public object Convert(object? value, Type t, object? p, CultureInfo c) => (value as string) switch
    {
        "Pass" => Pass,
        "Warning" => Warn,
        "Error" => Err,
        "Manual" or "External tool" => Manual,
        _ => NotRun
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    private static SolidColorBrush New(string h) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)!); b.Freeze(); return b; }
}

/// <summary>Hex string -> SolidColorBrush (for data-driven colours).</summary>
public sealed class StringToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString((string?)value ?? "#888888")!); }
        catch { return Brushes.Gray; }
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Match bool -> green/red chip.</summary>
public sealed class MatchToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Ok = New("#2E7D32");
    private static readonly SolidColorBrush No = New("#C62828");
    public object Convert(object? value, Type t, object? p, CultureInfo c) => (value is bool b && b) ? Ok : No;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    private static SolidColorBrush New(string h) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)!); b.Freeze(); return b; }
}

/// <summary>null -> Visible (used to show a "clean / none" placeholder), non-null -> Collapsed.</summary>
public sealed class NullToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Compare-row Match flag -> translucent red tint on mismatch, transparent on match.</summary>
public sealed class MismatchTintConverter : IValueConverter
{
    private static readonly SolidColorBrush Bad = New("#33C62828");   // translucent red (light+dark safe)
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => (value is bool b && !b) ? Bad : (Brush)Brushes.Transparent;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    private static SolidColorBrush New(string h) { var br = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)!); br.Freeze(); return br; }
}

/// <summary>CRC / Clock reuse state -> badge colour (reuse=green, new=amber, present=blue, n/a=grey).</summary>
public sealed class CrcStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Reuse = New("#2E7D32");
    private static readonly SolidColorBrush New_ = New("#EF8C00");
    private static readonly SolidColorBrush Present = New("#1565C0");
    private static readonly SolidColorBrush Na = New("#757575");
    public object Convert(object? value, Type t, object? p, CultureInfo c) => (value as string) switch
    {
        "reuse" => Reuse,
        "new" => New_,
        "present" => Present,
        _ => Na
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    private static SolidColorBrush New(string h) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)!); b.Freeze(); return b; }
}

/// <summary>Worst severity (nullable) -> subtle row background tint.</summary>
public sealed class WorstSeverityToRowBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ErrBg = New("#33C62828");   // translucent red (works on light+dark)
    private static readonly SolidColorBrush WarnBg = New("#30EF8C00");  // translucent amber
    private static readonly Brush None = Brushes.Transparent;

    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        Severity.Error => ErrBg,
        Severity.Warning => WarnBg,
        _ => None
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    private static SolidColorBrush New(string h) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)!); b.Freeze(); return b; }
}
