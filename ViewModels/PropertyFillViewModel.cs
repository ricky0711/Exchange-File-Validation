using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeFileValidator.Models;
using ExchangeFileValidator.Services;

namespace ExchangeFileValidator.ViewModels;

public partial class PropertyFillViewModel : ObservableObject
{
    private readonly PropertyFillService _svc = new();
    private ReferenceData? _data;

    public PropertyFillViewModel()
    {
        DemandsView = CollectionViewSource.GetDefaultView(Demands);
        DemandsView.Filter = Filter;
    }

    public ObservableCollection<IsrDemand> Demands { get; } = new();
    public ICollectionView DemandsView { get; }

    public string[] StatusFilters { get; } = { "All", "Filled", "Pending L2.1", "New" };
    public string[] SearchFields { get; } = { "All", "Signal", "Frame", "PDU" };

    [ObservableProperty] private string _selectedStatus = "All";
    [ObservableProperty] private string _selectedSearchField = "All";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _summary = "Load data, then Fill Properties.";

    public void SetData(ReferenceData data) { _data = data; Fill(); }

    [RelayCommand]
    private void Fill()
    {
        if (_data is null) return;
        _svc.Fill(_data.Demands, _data);
        Demands.Clear();
        foreach (var d in _data.Demands) Demands.Add(d);
        var c = PropertyFillService.Counts(_data.Demands);
        Summary = $"{_data.Demands.Count} demands  •  {c.filled} filled  {c.pending} pending L2.1  {c.newSig} new";
        DemandsView.Refresh();
    }

    private bool Filter(object obj)
    {
        if (obj is not IsrDemand d) return false;
        bool statusOk = SelectedStatus switch
        {
            "Filled" => d.FillSource.Length > 0,
            "Pending L2.1" => d.FillStatus.StartsWith("Load 2nd"),
            "New" => d.FillSource.Length == 0 && !d.FillStatus.StartsWith("Load 2nd"),
            _ => true
        };
        if (!statusOk) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;
        var q = Search.Trim();
        bool In(string f) => (f ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
        return SelectedSearchField switch
        {
            "Signal" => In(d.ParameterProposal),
            "Frame" => In(d.FilledFrame),
            "PDU" => In(d.FilledPdu),
            _ => In(d.ParameterProposal) || In(d.FilledFrame) || In(d.FilledPdu),
        };
    }

    partial void OnSelectedStatusChanged(string value) => DemandsView.Refresh();
    partial void OnSelectedSearchFieldChanged(string value) => DemandsView.Refresh();
    partial void OnSearchChanged(string value) => DemandsView.Refresh();
}
