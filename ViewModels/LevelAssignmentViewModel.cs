using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeFileValidator.Models;
using ExchangeFileValidator.Services;
using Microsoft.Win32;

namespace ExchangeFileValidator.ViewModels;

public partial class LevelAssignmentViewModel : ObservableObject
{
    private readonly ReferenceDataLoader _loader;
    private readonly LevelAssignmentService _svc = new();
    private ReferenceData? _data;
    private HashSet<string>? _secondArch;

    public LevelAssignmentViewModel(ReferenceDataLoader loader)
    {
        _loader = loader;
        DemandsView = CollectionViewSource.GetDefaultView(Demands);
        DemandsView.Filter = Filter;
    }

    public ObservableCollection<IsrDemand> Demands { get; } = new();
    public ICollectionView DemandsView { get; }

    public string[] LevelFilters { get; } = { "All", "L0", "L1", "L2", "L2.1", "L3" };
    public string[] SearchFields { get; } = { "All", "Signal", "ISR", "Tx", "Rx" };

    [ObservableProperty] private string _selectedFilter = "All";
    [ObservableProperty] private string _selectedSearchField = "All";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _summary = "Load data, then Assign Levels.";
    [ObservableProperty] private string _secondArchName = "(none)";

    /// <summary>Set by MainViewModel so loading a 2nd architecture also re-runs fill/validation.</summary>
    public Action? OnReferenceDataChanged { get; set; }

    public void SetData(ReferenceData data)
    {
        _data = data;
        _secondArch = data.SecondArchByName is null ? null
            : new HashSet<string>(data.SecondArchByName.Keys, StringComparer.OrdinalIgnoreCase);
        Assign();
    }

    [RelayCommand]
    private void Assign()
    {
        if (_data is null) return;
        _svc.Assign(_data.Demands, _data, _secondArch);
        Demands.Clear();
        foreach (var d in _data.Demands) Demands.Add(d);
        var c = LevelAssignmentService.Counts(_data.Demands);
        Summary = $"{_data.Demands.Count} demands  •  L0 {c.L0}  L1 {c.L1}  L2 {c.L2}  L2.1 {c.L21}  L3 {c.L3}"
                + (c.Unassigned > 0 ? $"  (unassigned {c.Unassigned})" : "");
        DemandsView.Refresh();
    }

    [RelayCommand]
    private void LoadSecondArchitecture()
    {
        if (_data is null) return;
        var dlg = new OpenFileDialog { Filter = "Message List (*.xlsx)|*.xlsx", Title = "Select 2nd architecture Message List (for Level 2.1)" };
        if (dlg.ShowDialog() != true) return;
        var defs = _loader.LoadSignalDefsByName(dlg.FileName);
        _data.SecondArchByName = defs;
        _secondArch = new HashSet<string>(defs.Keys, StringComparer.OrdinalIgnoreCase);
        SecondArchName = System.IO.Path.GetFileName(dlg.FileName) + $"  ({defs.Count:N0} signals)";
        Assign();
        OnReferenceDataChanged?.Invoke();   // re-fill + re-validate with the new arch
    }

    private bool Filter(object obj)
    {
        if (obj is not IsrDemand d) return false;
        if (SelectedFilter != "All" && d.LevelLabel != SelectedFilter) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;
        var q = Search.Trim();
        bool In(string field) => field.Contains(q, StringComparison.OrdinalIgnoreCase);
        return SelectedSearchField switch
        {
            "Signal" => In(d.ParameterProposal),
            "ISR" => In(d.IsrNumber),
            "Tx" => In(d.Emitter),
            "Rx" => In(d.Receiver),
            _ => In(d.ParameterProposal) || In(d.IsrNumber) || In(d.Emitter) || In(d.Receiver),
        };
    }

    partial void OnSelectedFilterChanged(string value) => DemandsView.Refresh();
    partial void OnSelectedSearchFieldChanged(string value) => DemandsView.Refresh();
    partial void OnSearchChanged(string value) => DemandsView.Refresh();
}
