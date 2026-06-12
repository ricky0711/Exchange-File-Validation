using System.Windows;
using System.Windows.Controls;

namespace ExchangeFileValidator.Controls;

public partial class ColumnFilterHeader : UserControl
{
    public ColumnFilterHeader() => InitializeComponent();

    // Toggle the filter popup without letting the click reach the DataGrid header's sort handler.
    private void Funnel_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ColumnFilterState s) s.IsPopupOpen = !s.IsPopupOpen;
        e.Handled = true;
    }
}
