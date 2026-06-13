using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using ExchangeFileValidator.Controls;
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

    /// <summary>Excel-grade per-column filtering, shared with the view's generated columns.</summary>
    public GridFilterController Controller { get; } = new();
    public ObservableCollection<ColumnVisibility> ColumnChooser { get; } = new();
    public List<ColumnSpec> ColumnSpecs { get; private set; } = new();

    /// <summary>Raised when the column set changes so the view rebuilds the grid columns.</summary>
    public event Action? ColumnsChanged;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _counts = "";

    public ReferenceBrowserViewModel()
    {
        SignalsView = CollectionViewSource.GetDefaultView(Signals);
        SignalsView.Filter = FilterSignal;
        Controller.FilterChanged += UpdateCounts;
        ColumnSpecs = BuildSpecs(Array.Empty<string>());
    }

    public void SetData(ReferenceData data)
    {
        _all = data.Signals;
        Signals.Clear();
        foreach (var s in _all) Signals.Add(s);   // virtualized grid keeps this cheap to render
        _total = data.Signals.Count;

        // Per-ECU node columns vary by file → rebuild the column set from the loaded signals.
        var ecuNames = _all.SelectMany(s => s.EcuTxRx.Keys)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                           .ToList();
        ColumnSpecs = BuildSpecs(ecuNames);
        Controller.SetSource(_all, SignalsView);
        ColumnsChanged?.Invoke();

        SignalsView.Refresh();
        UpdateCounts();
    }

    private static List<ColumnSpec> BuildSpecs(IReadOnlyList<string> ecuNames)
    {
        string S(object o, Func<SignalDef, string> f) => o is SignalDef s ? f(s) : "";
        var specs = new List<ColumnSpec>
        {
            new() { Header = "Signal", BindingPath = nameof(SignalDef.SignalName), Width = 240, Accessor = o => S(o, x => x.SignalName) },
            new() { Header = "Frame", BindingPath = nameof(SignalDef.FrameName), Width = 160, Accessor = o => S(o, x => x.FrameName) },
            new() { Header = "ID", BindingPath = nameof(SignalDef.FrameIdHex), Width = 70, Accessor = o => S(o, x => x.FrameIdHex) },
            new() { Header = "Frame Type", BindingPath = nameof(SignalDef.FrameType), Width = 130, VisibleByDefault = false, Accessor = o => S(o, x => x.FrameType) },
            new() { Header = "Frame Container", BindingPath = nameof(SignalDef.FrameContainer), Width = 150, VisibleByDefault = false, Accessor = o => S(o, x => x.FrameContainer) },
            new() { Header = "PDU", BindingPath = nameof(SignalDef.PduName), Width = 160, Accessor = o => S(o, x => x.PduName) },
            new() { Header = "Byte Pos", BindingPath = nameof(SignalDef.BytePosition), Width = 70, VisibleByDefault = false, Accessor = o => S(o, x => x.BytePosition) },
            new() { Header = "Bit Pos", BindingPath = nameof(SignalDef.BitPosition), Width = 70, VisibleByDefault = false, Accessor = o => S(o, x => x.BitPosition) },
            new() { Header = "Bits", BindingPath = nameof(SignalDef.SignalSizeBits), Width = 55, Accessor = o => S(o, x => x.SignalSizeBits?.ToString() ?? "") },
            new() { Header = "Type", BindingPath = nameof(SignalDef.ValueType), Width = 90, Accessor = o => S(o, x => x.ValueType) },
            new() { Header = "Coding", BindingPath = nameof(SignalDef.Coding), Width = 120, VisibleByDefault = false, Accessor = o => S(o, x => x.Coding) },
            new() { Header = "Meaning", BindingPath = nameof(SignalDef.Meaning), Width = 200, VisibleByDefault = false, Accessor = o => S(o, x => x.Meaning) },
            new() { Header = "Unit", BindingPath = nameof(SignalDef.Unit), Width = 70, Accessor = o => S(o, x => x.Unit) },
            new() { Header = "Resolution", BindingPath = nameof(SignalDef.Resolution), Width = 90, VisibleByDefault = false, Accessor = o => S(o, x => x.Resolution) },
            new() { Header = "Offset", BindingPath = nameof(SignalDef.Offset), Width = 80, VisibleByDefault = false, Accessor = o => S(o, x => x.Offset) },
            new() { Header = "Min", BindingPath = nameof(SignalDef.Min), Width = 70, Accessor = o => S(o, x => x.Min) },
            new() { Header = "Max", BindingPath = nameof(SignalDef.Max), Width = 70, Accessor = o => S(o, x => x.Max) },
            new() { Header = "Tx Type", BindingPath = nameof(SignalDef.TransmissionType), Width = 120, VisibleByDefault = false, Accessor = o => S(o, x => x.TransmissionType) },
            new() { Header = "Period", BindingPath = nameof(SignalDef.Period), Width = 70, VisibleByDefault = false, Accessor = o => S(o, x => x.Period) },
            new() { Header = "Excl. Time", BindingPath = nameof(SignalDef.ExclTime), Width = 80, VisibleByDefault = false, Accessor = o => S(o, x => x.ExclTime) },
        };

        // Per-ECU T/R node columns (hidden by default; user toggles them on via the Columns chooser).
        foreach (var ecu in ecuNames)
        {
            var name = ecu;
            specs.Add(new ColumnSpec
            {
                Header = name,
                BindingPath = $"EcuTxRx[{name}]",
                Width = 64,
                VisibleByDefault = false,
                Accessor = o => o is SignalDef s && s.EcuTxRx.TryGetValue(name, out var v) ? v : "",
            });
        }
        return specs;
    }

    private bool FilterSignal(object obj)
    {
        if (!Controller.Pass(obj)) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;
        var q = Search.Trim();
        foreach (var spec in ColumnSpecs)
            if (spec.Accessor(obj).Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    partial void OnSearchChanged(string value) { SignalsView.Refresh(); UpdateCounts(); }
}
