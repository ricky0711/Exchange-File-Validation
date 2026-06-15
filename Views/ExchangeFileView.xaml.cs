using System.Windows.Controls;
using ExchangeFileValidator.Controls;
using ExchangeFileValidator.ViewModels;

namespace ExchangeFileValidator.Views;

public partial class ExchangeFileView : UserControl
{
    private bool _built;

    public ExchangeFileView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Keep the selected row visible when Prev/Next moves the selection (Fix 5).
        Grid.SelectionChanged += (_, _) => { if (Grid.SelectedItem is not null) Grid.ScrollIntoView(Grid.SelectedItem); };
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        // The Exchange File column set is fixed, so build the grid columns once.
        if (_built || DataContext is not ExchangeFileViewModel vm) return;
        ExcelGridBuilder.Build(Grid, vm.ColumnSpecs, vm.Controller, vm.ColumnChooser);
        _built = true;
    }
}
