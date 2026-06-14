using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeFileValidator.Services;

namespace ExchangeFileValidator.ViewModels;

/// <summary>Feature 1 — hierarchical PDU → Frame → Signal → ISR explorer (lazy + virtualized).</summary>
public partial class ExplorerViewModel : ObservableObject
{
    private ReferenceData? _data;

    public ExplorerViewModel()
    {
        _groupMode = GroupModes[0];
        RootsView = CollectionViewSource.GetDefaultView(Roots);
        RootsView.Filter = Filter;
    }

    public ObservableCollection<ExplorerNode> Roots { get; } = new();
    public ICollectionView RootsView { get; }

    public string[] GroupModes { get; } = { "PDU → Frame → Signal → ISR", "Frame → Signal → ISR", "Signal → ISR" };
    [ObservableProperty] private string _groupMode;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _summary = "";

    /// <summary>Set by MainViewModel: open a frame in the Frame-layout view (Feature 2).</summary>
    public Action<string>? OnOpenFrame { get; set; }
    /// <summary>Set by MainViewModel: open an ISR's signal in the trace.</summary>
    public Action<IsrNode>? OnOpenTrace { get; set; }

    public void SetData(ReferenceData data) { _data = data; BuildRoots(); }

    partial void OnGroupModeChanged(string value) => BuildRoots();
    partial void OnSearchChanged(string value) { RootsView.Refresh(); UpdateSummary(); }

    private void BuildRoots()
    {
        Roots.Clear();
        if (_data is null) { UpdateSummary(); return; }
        var d = _data;
        IEnumerable<ExplorerNode> nodes =
            GroupMode.StartsWith("Frame") ? d.SignalsByFrame.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(f => (ExplorerNode)new FrameNode(d, f))
          : GroupMode.StartsWith("Signal") ? d.SignalByName.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(s => (ExplorerNode)new SignalNode(d, d.SignalByName[s]))
          : d.FramesByPdu.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(p => (ExplorerNode)new PduNode(d, p));

        foreach (var n in nodes) Roots.Add(n);
        RootsView.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int total = Roots.Count;
        int shown = RootsView.Cast<object>().Count();
        var kind = GroupMode.StartsWith("Frame") ? "frames" : GroupMode.StartsWith("Signal") ? "signals" : "PDUs";
        Summary = shown == total ? $"{total:N0} {kind}" : $"{shown:N0} of {total:N0} {kind} (filtered)";
    }

    private bool Filter(object o)
    {
        if (string.IsNullOrWhiteSpace(Search)) return true;
        return o is ExplorerNode n && n.SearchText.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand] private void OpenFrame(FrameNode? node) { if (node is not null) OnOpenFrame?.Invoke(node.Name); }
    [RelayCommand] private void OpenTrace(IsrNode? node) { if (node is not null) OnOpenTrace?.Invoke(node); }
}
