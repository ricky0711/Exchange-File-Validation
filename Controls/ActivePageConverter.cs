using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace ExchangeFileValidator.Controls;

/// <summary>Highlights the current sidebar entry: active page key -> Secondary appearance, otherwise Transparent.</summary>
public sealed class ActivePageToAppearanceConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal)
            ? ControlAppearance.Secondary
            : ControlAppearance.Transparent;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
