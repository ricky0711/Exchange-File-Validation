using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeFileValidator.Controls;
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
        Controller.FilterChanged += UpdateSummary;
        ColumnSpecs = BuildSpecs();
    }

    public ObservableCollection<IsrDemand> Demands { get; } = new();
    public ICollectionView DemandsView { get; }

    /// <summary>Excel-grade per-column filtering for the Exchange File grid.</summary>
    public GridFilterController Controller { get; } = new();
    public ObservableCollection<ColumnVisibility> ColumnChooser { get; } = new();
    public List<ColumnSpec> ColumnSpecs { get; }

    public string[] StatusFilters { get; } = { "All", "Clean", "Warning", "Error" };
    public string[] LevelFilters { get; } = { "All", "L0", "L1", "L2", "L2.1", "L3" };

    [ObservableProperty] private string _selectedStatus = "All";
    [ObservableProperty] private string _selectedLevel = "All";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _pipelineSummary = "";
    [ObservableProperty] private string _secondArchName = "(none)";
    [ObservableProperty] private IsrDemand? _selectedDemand;
    [ObservableProperty] private SignalDef? _atDef;

    /// <summary>Set by MainViewModel: re-assign levels + re-fill properties + re-validate (merged Level/Fill pages).</summary>
    public Action? OnRerun { get; set; }
    /// <summary>Set by MainViewModel: pick a 2nd-architecture Message List to resolve Level 2.1.</summary>
    public Action? OnLoadSecondArch { get; set; }

    [RelayCommand] private void Rerun() => OnRerun?.Invoke();
    [RelayCommand] private void LoadSecondArch() => OnLoadSecondArch?.Invoke();

    /// <summary>Level distribution + fill status line (the old Level/Fill page summaries, merged here).</summary>
    public void UpdatePipelineSummary(ReferenceData data)
    {
        var lc = LevelAssignmentService.Counts(data.Demands);
        var fc = PropertyFillService.Counts(data.Demands);
        PipelineSummary =
            $"Levels — L0 {lc.L0}  L1 {lc.L1}  L2 {lc.L2}  L2.1 {lc.L21}  L3 {lc.L3}"
            + (lc.Unassigned > 0 ? $"  (unassigned {lc.Unassigned})" : "")
            + $"     Fill — {fc.filled} filled  {fc.pending} pending L2.1  {fc.newSig} new";
    }

    [ObservableProperty] private string _positionText = "";

    partial void OnSelectedDemandChanged(IsrDemand? value)
    {
        AtDef = null;
        if (value is not null && _data is not null)
        {
            if (_data.SignalByName.TryGetValue(value.ParameterProposal ?? "", out var sig)) AtDef = sig;
            else if (_data.SecondArchByName is not null
                     && _data.SecondArchByName.TryGetValue(value.ParameterProposal ?? "", out var sig2)) AtDef = sig2;
        }
        UpdatePosition();
    }

    private List<IsrDemand> Visible() => DemandsView.Cast<IsrDemand>().ToList();
    private int CurrentIndex() => SelectedDemand is null ? -1 : Visible().IndexOf(SelectedDemand);

    private void UpdatePosition()
    {
        var vis = Visible();
        int idx = SelectedDemand is null ? -1 : vis.IndexOf(SelectedDemand);
        PositionText = idx >= 0 ? $"{idx + 1} / {vis.Count}" : (vis.Count > 0 ? $"– / {vis.Count}" : "0 / 0");
        NextCommand.NotifyCanExecuteChanged();
        PrevCommand.NotifyCanExecuteChanged();
    }

    private bool CanNext() { int i = CurrentIndex(); return i >= 0 && i < Visible().Count - 1; }
    private bool CanPrev() => CurrentIndex() > 0;

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void Next() { var vis = Visible(); int i = vis.IndexOf(SelectedDemand!); if (i >= 0 && i < vis.Count - 1) SelectedDemand = vis[i + 1]; }

    [RelayCommand(CanExecute = nameof(CanPrev))]
    private void Prev() { var vis = Visible(); int i = vis.IndexOf(SelectedDemand!); if (i > 0) SelectedDemand = vis[i - 1]; }

    public void SetData(ReferenceData data)
    {
        _data = data;
        Demands.Clear();
        foreach (var d in data.Demands) Demands.Add(d);
        Controller.SetSource(data.Demands, DemandsView);
        Refresh(data);
    }

    public void Refresh(ReferenceData data) { DemandsView.Refresh(); UpdateSummary(); }

    private static List<ColumnSpec> BuildSpecs()
    {
        string S(object o, Func<IsrDemand, string> f) => o is IsrDemand d ? f(d) : "";
        return new List<ColumnSpec>
        {
            new() { Header = "Lvl", BindingPath = nameof(IsrDemand.Level), Width = 60, Filterable = false, CellTemplateKey = "LevelChipCell", Accessor = o => S(o, x => x.LevelLabel) },
            new() { Header = "ISR Number", BindingPath = nameof(IsrDemand.IsrNumber), Width = 130, Accessor = o => S(o, x => x.IsrNumber) },
            new() { Header = "Feature", BindingPath = nameof(IsrDemand.FeatureNumber), Width = 100, VisibleByDefault = false, Accessor = o => S(o, x => x.FeatureNumber) },
            new() { Header = "EmCode", BindingPath = nameof(IsrDemand.EmitterCode), Width = 80, VisibleByDefault = false, Accessor = o => S(o, x => x.EmitterCode) },
            new() { Header = "Emitter", BindingPath = nameof(IsrDemand.Emitter), Width = 110, Accessor = o => S(o, x => x.Emitter) },
            new() { Header = "RxCode", BindingPath = nameof(IsrDemand.ReceiverCode), Width = 80, VisibleByDefault = false, Accessor = o => S(o, x => x.ReceiverCode) },
            new() { Header = "Receiver", BindingPath = nameof(IsrDemand.Receiver), Width = 110, Accessor = o => S(o, x => x.Receiver) },
            new() { Header = "Signal (Proposal)", BindingPath = nameof(IsrDemand.ParameterProposal), Width = 220, Accessor = o => S(o, x => x.ParameterProposal) },
            new() { Header = "Frame", BindingPath = nameof(IsrDemand.FilledFrame), Width = 150, Accessor = o => S(o, x => x.FilledFrame) },
            new() { Header = "PDU", BindingPath = nameof(IsrDemand.FilledPdu), Width = 150, Accessor = o => S(o, x => x.FilledPdu) },
            new() { Header = "Bits", BindingPath = nameof(IsrDemand.FilledBits), Width = 55, Accessor = o => S(o, x => x.FilledBits?.ToString() ?? "") },
            new() { Header = "CRC/CLK", BindingPath = nameof(IsrDemand.CrcClk), Width = 80, Accessor = o => S(o, x => x.CrcClk) },
            new() { Header = "Media", BindingPath = nameof(IsrDemand.MediaType), Width = 90, VisibleByDefault = false, Accessor = o => S(o, x => x.MediaType) },
            new() { Header = "Net", BindingPath = nameof(IsrDemand.NetworkType), Width = 80, VisibleByDefault = false, Accessor = o => S(o, x => x.NetworkType) },
            new() { Header = "Loss ASIL", BindingPath = nameof(IsrDemand.LossLinkageAsil), Width = 80, VisibleByDefault = false, Accessor = o => S(o, x => x.LossLinkageAsil) },
            new() { Header = "Corrupt ASIL", BindingPath = nameof(IsrDemand.CorruptDataAsil), Width = 90, VisibleByDefault = false, Accessor = o => S(o, x => x.CorruptDataAsil) },
            new() { Header = "UpdateTime", BindingPath = nameof(IsrDemand.UpdateTime), Width = 100, Accessor = o => S(o, x => x.UpdateTime) },
            new() { Header = "Synthesis Status", BindingPath = nameof(IsrDemand.SynthesisStatus), Width = 120, VisibleByDefault = false, Accessor = o => S(o, x => x.SynthesisStatus) },
            new() { Header = "Logical Data", BindingPath = nameof(IsrDemand.LogicalData), Width = 150, VisibleByDefault = false, Accessor = o => S(o, x => x.LogicalData) },
            new() { Header = "Analog Data", BindingPath = nameof(IsrDemand.AnalogData), Width = 150, VisibleByDefault = false, Accessor = o => S(o, x => x.AnalogData) },
            new() { Header = "Kind Of Isr", BindingPath = nameof(IsrDemand.KindOfIsr), Width = 100, VisibleByDefault = false, Accessor = o => S(o, x => x.KindOfIsr) },
            new() { Header = "Other Requirements", BindingPath = nameof(IsrDemand.OtherRequirements), Width = 150, VisibleByDefault = false, Accessor = o => S(o, x => x.OtherRequirements) },
            new() { Header = "Issues", BindingPath = nameof(IsrDemand.Issues), Star = true, Width = 360, Accessor = o => S(o, x => x.Issues) },
        };
    }

    private void UpdateSummary()
    {
        var vis = DemandsView.Cast<IsrDemand>().ToList();
        int err = vis.Count(d => d.WorstSeverity == Severity.Error);
        int warn = vis.Count(d => d.WorstSeverity == Severity.Warning);
        int clean = vis.Count(d => d.WorstSeverity is null);
        Summary = $"{vis.Count} demands (filtered)  •  {clean} clean, {warn} warning, {err} error";
        UpdatePosition();
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
        if (!Controller.Pass(obj)) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;
        var q = Search.Trim();
        foreach (var spec in ColumnSpecs)
            if (spec.Accessor(obj).Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    partial void OnSelectedStatusChanged(string value) { DemandsView.Refresh(); UpdateSummary(); }
    partial void OnSelectedLevelChanged(string value) { DemandsView.Refresh(); UpdateSummary(); }
    partial void OnSearchChanged(string value) { DemandsView.Refresh(); UpdateSummary(); }
}
