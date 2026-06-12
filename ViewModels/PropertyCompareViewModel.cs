using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeFileValidator.Models;
using ExchangeFileValidator.Services;

namespace ExchangeFileValidator.ViewModels;

public enum CompareMode { SignalProperties, OtherRequirements }

public partial class PropertyCompareViewModel : ObservableObject
{
    private readonly PropertyComparisonService _svc = new();
    private ReferenceData? _data;

    public PropertyCompareViewModel(CompareMode mode, string title)
    {
        Mode = mode; Title = title;
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = Filter;
    }

    public CompareMode Mode { get; }
    public string Title { get; }

    public ObservableCollection<CompareRow> Rows { get; } = new();
    public ICollectionView RowsView { get; }

    [ObservableProperty] private bool _mismatchesOnly = true;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _summary = "";

    public void SetData(ReferenceData data) { _data = data; Run(); }

    [RelayCommand]
    private void Run()
    {
        if (_data is null) return;
        var rows = Mode == CompareMode.SignalProperties
            ? _svc.CompareSignalProperties(_data.Demands, _data, MismatchesOnly)
            : _svc.CompareOtherRequirements(_data.Demands, _data, MismatchesOnly);
        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var visible = RowsView.Cast<CompareRow>().ToList();
        int mis = visible.Count(r => !r.Match);
        Summary = MismatchesOnly
            ? $"{mis} mismatches (filtered)"
            : $"{visible.Count} comparisons · {mis} mismatches (filtered)";
    }

    partial void OnMismatchesOnlyChanged(bool value) => Run();

    private bool Filter(object obj)
    {
        if (obj is not CompareRow r) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;
        var q = Search.Trim();
        return r.Isr.Contains(q, StringComparison.OrdinalIgnoreCase)
            || r.Signal.Contains(q, StringComparison.OrdinalIgnoreCase)
            || r.Property.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSearchChanged(string value) { RowsView.Refresh(); UpdateSummary(); }
}
