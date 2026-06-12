using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using ExchangeFileValidator.Models;
using ExchangeFileValidator.Services;

namespace ExchangeFileValidator.ViewModels;

public partial class ExchangeFileViewModel : ObservableObject
{
    private ReferenceData? _data;

    public ExchangeFileViewModel()
    {
        DemandsView = CollectionViewSource.GetDefaultView(Demands);
        DemandsView.Filter = Filter;
    }

    public ObservableCollection<IsrDemand> Demands { get; } = new();
    public ICollectionView DemandsView { get; }

    public string[] StatusFilters { get; } = { "All", "Clean", "Warning", "Error" };
    public string[] LevelFilters { get; } = { "All", "L0", "L1", "L2", "L2.1", "L3" };
    public string[] SearchFields { get; } = { "All", "Signal", "ISR", "Frame", "Tx", "Rx" };

    [ObservableProperty] private string _selectedStatus = "All";
    [ObservableProperty] private string _selectedLevel = "All";
    [ObservableProperty] private string _selectedSearchField = "All";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private IsrDemand? _selectedDemand;
    [ObservableProperty] private SignalDef? _atDef;

    partial void OnSelectedDemandChanged(IsrDemand? value)
    {
        AtDef = null;
        if (value is null || _data is null) return;
        if (_data.SignalByName.TryGetValue(value.ParameterProposal ?? "", out var sig)) AtDef = sig;
        else if (_data.SecondArchByName is not null
                 && _data.SecondArchByName.TryGetValue(value.ParameterProposal ?? "", out var sig2)) AtDef = sig2;
    }

    public void SetData(ReferenceData data)
    {
        _data = data;
        Demands.Clear();
        foreach (var d in data.Demands) Demands.Add(d);
        Refresh(data);
    }

    public void Refresh(ReferenceData data) { DemandsView.Refresh(); UpdateSummary(); }

    private void UpdateSummary()
    {
        var vis = DemandsView.Cast<IsrDemand>().ToList();
        int err = vis.Count(d => d.WorstSeverity == Severity.Error);
        int warn = vis.Count(d => d.WorstSeverity == Severity.Warning);
        int clean = vis.Count(d => d.WorstSeverity is null);
        Summary = $"{vis.Count} demands (filtered)  •  {clean} clean, {warn} warning, {err} error";
    }

    private bool Filter(object obj)
    {
        if (obj is not IsrDemand d) return false;
        bool statusOk = SelectedStatus switch
        {
            "Clean" => d.WorstSeverity is null,
            "Warning" => d.WorstSeverity == Severity.Warning,
            "Error" => d.WorstSeverity == Severity.Error,
            _ => true
        };
        if (!statusOk) return false;
        if (SelectedLevel != "All" && d.LevelLabel != SelectedLevel) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;
        var q = Search.Trim();
        bool In(string f) => (f ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
        return SelectedSearchField switch
        {
            "Signal" => In(d.ParameterProposal), "ISR" => In(d.IsrNumber),
            "Frame" => In(string.IsNullOrEmpty(d.FilledFrame) ? d.Frame : d.FilledFrame),
            "Tx" => In(d.Emitter), "Rx" => In(d.Receiver),
            _ => In(d.ParameterProposal) || In(d.IsrNumber) || In(d.Emitter) || In(d.Receiver),
        };
    }

    partial void OnSelectedStatusChanged(string value) { DemandsView.Refresh(); UpdateSummary(); }
    partial void OnSelectedLevelChanged(string value) { DemandsView.Refresh(); UpdateSummary(); }
    partial void OnSelectedSearchFieldChanged(string value) { DemandsView.Refresh(); UpdateSummary(); }
    partial void OnSearchChanged(string value) { DemandsView.Refresh(); UpdateSummary(); }
}
