using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using ExchangeFileValidator.Models;
using ExchangeFileValidator.Services;

namespace ExchangeFileValidator.ViewModels;

public partial class ReferenceBrowserViewModel : ObservableObject
{
    private List<SignalDef> _all = new();
    private int _total;
    private void UpdateCounts() { var n = SignalsView.Cast<SignalDef>().Count(); Counts = n == _total ? $"{_total:N0} signals" : $"{n:N0} of {_total:N0} signals (filtered)"; }

    public ObservableCollection<SignalDef> Signals { get; } = new();
    public ICollectionView SignalsView { get; }

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _counts = "";

    public string[] SearchFields { get; } = { "All", "Signal", "Frame", "PDU" };
    [ObservableProperty] private string _selectedSearchField = "All";

    public ReferenceBrowserViewModel()
    {
        SignalsView = CollectionViewSource.GetDefaultView(Signals);
        SignalsView.Filter = FilterSignal;
    }

    public void SetData(ReferenceData data)
    {
        _all = data.Signals;
        Signals.Clear();
        foreach (var s in _all) Signals.Add(s);   // virtualized grid keeps this cheap to render
        _total = data.Signals.Count;
        SignalsView.Refresh();
        UpdateCounts();
    }

    private bool FilterSignal(object obj)
    {
        if (string.IsNullOrWhiteSpace(Search)) return true;
        if (obj is not SignalDef s) return false;
        var q = Search.Trim();
        bool In(string field) => field.Contains(q, StringComparison.OrdinalIgnoreCase);
        return SelectedSearchField switch
        {
            "Signal" => In(s.SignalName),
            "Frame" => In(s.FrameName),
            "PDU" => In(s.PduName),
            _ => In(s.SignalName) || In(s.FrameName) || In(s.PduName),
        };
    }

    partial void OnSearchChanged(string value) { SignalsView.Refresh(); UpdateCounts(); }
    partial void OnSelectedSearchFieldChanged(string value) { SignalsView.Refresh(); UpdateCounts(); }
}
