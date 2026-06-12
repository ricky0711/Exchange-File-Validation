using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ExchangeFileValidator.Controls;

/// <summary>One tickable distinct value inside a column's Excel-style filter popup.</summary>
public partial class DistinctValue : ObservableObject
{
    public DistinctValue(string display) => Display = display;
    public string Display { get; }
    [ObservableProperty] private bool _isChecked = true;
}

/// <summary>
/// Declarative description of one grid column. Pure data (no WPF visual types) so view-models can
/// build the column set; <see cref="ExcelGridBuilder"/> turns it into a real <see cref="DataGridColumn"/>.
/// </summary>
public sealed class ColumnSpec
{
    public string Header { get; init; } = "";
    /// <summary>Binding path used for the cell display AND for sorting.</summary>
    public string BindingPath { get; init; } = "";
    /// <summary>Extracts the column's text from a row item, for distinct-value filtering + global search.</summary>
    public Func<object, string> Accessor { get; init; } = _ => "";
    public double Width { get; init; } = 120;
    public bool Star { get; init; }
    public bool VisibleByDefault { get; init; } = true;
    /// <summary>When false, no filter funnel is shown (e.g. a chip/badge column).</summary>
    public bool Filterable { get; init; } = true;
    /// <summary>Optional resource key of a custom cell <c>DataTemplate</c> (chip columns etc.).</summary>
    public string? CellTemplateKey { get; init; }
}

/// <summary>Per-column filter state: the distinct checkbox list, the in-popup search, and the applied exclusion set.</summary>
public partial class ColumnFilterState : ObservableObject
{
    private readonly Func<object, string> _accessor;
    private List<DistinctValue> _allValues = new();
    private HashSet<string> _excluded = new(StringComparer.Ordinal);
    private bool _built;

    public ColumnFilterState(string header, Func<object, string> accessor)
    {
        Header = header;
        _accessor = accessor;
    }

    public string Header { get; }

    /// <summary>Distinct values currently shown in the popup (already narrowed by the in-popup search).</summary>
    public ObservableCollection<DistinctValue> Values { get; } = new();

    [ObservableProperty] private string _popupSearch = "";
    [ObservableProperty] private bool _isPopupOpen;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _valueCount = "";

    /// <summary>Set by the controller; lazily computes this column's distinct values from the live source.</summary>
    public Func<ColumnFilterState, IEnumerable<string>>? DistinctSource { get; set; }

    /// <summary>Raised when the applied filter changes so the controller can refresh the view.</summary>
    public event Action? Changed;

    public string ValueOf(object item) => _accessor(item) ?? "";

    /// <summary>An item passes when its value is not in the (un-ticked) exclusion set.</summary>
    public bool Pass(object item) => _excluded.Count == 0 || !_excluded.Contains(ValueOf(item));

    /// <summary>Drops the cached distinct list so it is recomputed next time the popup opens.</summary>
    public void Invalidate() => _built = false;

    partial void OnIsPopupOpenChanged(bool value)
    {
        if (value) EnsureBuilt();
    }

    partial void OnPopupSearchChanged(string value) => ApplySearch();

    private void EnsureBuilt()
    {
        if (_built) return;
        var distinct = DistinctSource?.Invoke(this) ?? Enumerable.Empty<string>();
        _allValues = distinct
            .Select(d => new DistinctValue(d) { IsChecked = !_excluded.Contains(d) })
            .ToList();
        _built = true;
        ApplySearch();
    }

    private void ApplySearch()
    {
        Values.Clear();
        IEnumerable<DistinctValue> src = _allValues;
        var q = PopupSearch?.Trim();
        if (!string.IsNullOrEmpty(q))
            src = src.Where(v => v.Display.Contains(q, StringComparison.OrdinalIgnoreCase));
        foreach (var v in src) Values.Add(v);
        ValueCount = _allValues.Count == Values.Count ? $"{_allValues.Count}" : $"{Values.Count} of {_allValues.Count}";
    }

    [RelayCommand] private void SelectAll() { foreach (var v in Values) v.IsChecked = true; }
    [RelayCommand] private void ClearAll() { foreach (var v in Values) v.IsChecked = false; }

    [RelayCommand]
    private void Apply()
    {
        // Values reference the same instances as _allValues, so their IsChecked state is already current.
        _excluded = new HashSet<string>(
            _allValues.Where(v => !v.IsChecked).Select(v => v.Display), StringComparer.Ordinal);
        IsActive = _excluded.Count > 0;
        IsPopupOpen = false;
        Changed?.Invoke();
    }

    [RelayCommand]
    private void ClearColumnFilter()
    {
        _excluded.Clear();
        foreach (var v in _allValues) v.IsChecked = true;
        IsActive = false;
        IsPopupOpen = false;
        Changed?.Invoke();
    }
}

/// <summary>One row in the "Columns" show/hide chooser.</summary>
public partial class ColumnVisibility : ObservableObject
{
    public ColumnVisibility(string header, bool visible) { Header = header; _isVisible = visible; }
    public string Header { get; }
    [ObservableProperty] private bool _isVisible;
}

/// <summary>
/// Owns the per-column filter states for one grid, combines them (AND), and exposes a single
/// <see cref="Pass"/> predicate the page's <see cref="ICollectionView"/> filter calls. Distinct values
/// are computed lazily (only when a popup opens) and capped, so the 27k-row grid stays responsive.
/// </summary>
public sealed class GridFilterController
{
    private IReadOnlyList<object> _items = Array.Empty<object>();
    private ICollectionView? _view;
    private const int DistinctCap = 2000;

    public List<ColumnFilterState> Columns { get; } = new();

    /// <summary>Raised after any column filter is applied/cleared (for count refresh).</summary>
    public event Action? FilterChanged;

    public bool AnyActive => Columns.Any(c => c.IsActive);

    /// <summary>Clears all registered column states (call before rebuilding columns).</summary>
    public void Reset() => Columns.Clear();

    public ColumnFilterState Register(ColumnSpec spec)
    {
        var state = new ColumnFilterState(spec.Header, spec.Accessor) { DistinctSource = ComputeDistinct };
        state.Changed += OnColumnChanged;
        Columns.Add(state);
        return state;
    }

    public void SetSource(IReadOnlyList<object> items, ICollectionView view)
    {
        _items = items;
        _view = view;
        foreach (var c in Columns) c.Invalidate();
    }

    public bool Pass(object item)
    {
        for (int i = 0; i < Columns.Count; i++)
            if (!Columns[i].Pass(item)) return false;
        return true;
    }

    private void OnColumnChanged()
    {
        _view?.Refresh();
        FilterChanged?.Invoke();
    }

    private IEnumerable<string> ComputeDistinct(ColumnFilterState state)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _items)
        {
            set.Add(state.ValueOf(item));
            if (set.Count >= DistinctCap) break;   // cap keeps huge columns responsive
        }
        return set;
    }
}

/// <summary>Builds real <see cref="DataGridColumn"/>s from <see cref="ColumnSpec"/>s and wires the funnel header + chooser.</summary>
public static class ExcelGridBuilder
{
    public static void Build(
        DataGrid grid,
        IReadOnlyList<ColumnSpec> specs,
        GridFilterController controller,
        ObservableCollection<ColumnVisibility> chooser)
    {
        grid.Columns.Clear();
        controller.Reset();
        chooser.Clear();

        foreach (var spec in specs)
        {
            DataGridColumn col;
            if (spec.CellTemplateKey is { Length: > 0 } key && grid.TryFindResource(key) is DataTemplate tpl)
                col = new DataGridTemplateColumn { CellTemplate = tpl, SortMemberPath = spec.BindingPath };
            else
                col = new DataGridTextColumn { Binding = new Binding(spec.BindingPath) };

            col.Width = spec.Star
                ? new DataGridLength(1, DataGridLengthUnitType.Star)
                : new DataGridLength(spec.Width);
            col.Visibility = spec.VisibleByDefault ? Visibility.Visible : Visibility.Collapsed;
            if (!string.IsNullOrEmpty(spec.BindingPath)) col.SortMemberPath = spec.BindingPath;

            if (spec.Filterable)
            {
                var state = controller.Register(spec);
                col.Header = new ColumnFilterHeader { DataContext = state };
            }
            else
            {
                col.Header = spec.Header;
            }

            grid.Columns.Add(col);

            var capturedCol = col;
            var cv = new ColumnVisibility(spec.Header, spec.VisibleByDefault);
            cv.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ColumnVisibility.IsVisible))
                    capturedCol.Visibility = cv.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            };
            chooser.Add(cv);
        }
    }
}
