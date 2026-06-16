using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
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

        // Toggle (collapse) row details when clicking an already selected row
        Grid.PreviewMouseLeftButtonDown += (s, e) =>
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && !(dep is DataGridRow))
            {
                if (dep is DataGridDetailsPresenter)
                {
                    // Clicked inside the details presenter panel; do not collapse.
                    return;
                }
                dep = VisualTreeHelper.GetParent(dep);
            }
            if (dep is DataGridRow row)
            {
                if (row.IsSelected)
                {
                    row.IsSelected = false;
                    e.Handled = true;
                }
            }
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // The Exchange File column set is fixed, so build the grid columns once.
        if (_built || DataContext is not ExchangeFileViewModel vm) return;
        ExcelGridBuilder.Build(Grid, vm.ColumnSpecs, vm.Controller, vm.ColumnChooser);
        _built = true;
    }
}
