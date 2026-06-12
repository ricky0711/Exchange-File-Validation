using System.Windows.Controls;
using ExchangeFileValidator.Controls;
using ExchangeFileValidator.ViewModels;

namespace ExchangeFileValidator.Views;

public partial class ReferenceBrowserView : UserControl
{
    private ReferenceBrowserViewModel? _vm;

    public ReferenceBrowserView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.ColumnsChanged -= RebuildColumns;
        _vm = DataContext as ReferenceBrowserViewModel;
        if (_vm is not null)
        {
            _vm.ColumnsChanged += RebuildColumns;
            RebuildColumns();
        }
    }

    private void RebuildColumns()
    {
        if (_vm is null) return;
        ExcelGridBuilder.Build(Grid, _vm.ColumnSpecs, _vm.Controller, _vm.ColumnChooser);
    }
}
