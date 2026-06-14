using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeFileValidator.Services;

namespace ExchangeFileValidator.ViewModels;

/// <summary>Feature 2 — frame bit/byte layout view-model.</summary>
public partial class FrameViewModel : ObservableObject
{
    private readonly FrameLayoutService _svc = new();
    private ReferenceData? _data;

    [ObservableProperty] private FrameLayout? _layout;
    [ObservableProperty] private SignalBlock? _selectedBlock;
    [ObservableProperty] private string _frameSearch = "";
    [ObservableProperty] private string _detail = "Select a signal block to see its details.";
    [ObservableProperty] private string _status = "Open a frame from the Explorer, or search a frame name.";

    /// <summary>Raised when a new layout is loaded so the view rebuilds the bit/byte grid.</summary>
    public event Action? LayoutChanged;

    public void SetData(ReferenceData data) => _data = data;

    public void Load(string frame, ReferenceData data)
    {
        _data = data;
        Layout = _svc.Build(frame, data);
        SelectedBlock = null;
        Status = Layout.HasFrame ? Layout.Usage : $"Frame '{frame}' not found in the Message List.";
        LayoutChanged?.Invoke();
    }

    [RelayCommand]
    private void Search()
    {
        if (_data is null || string.IsNullOrWhiteSpace(FrameSearch)) return;
        Load(FrameSearch.Trim(), _data);
    }

    partial void OnSelectedBlockChanged(SignalBlock? value) => Detail = BuildDetail(value);

    private string BuildDetail(SignalBlock? b)
    {
        if (b is null) return "Select a signal block to see its details.";
        var s = b.Sig;
        var sb = new StringBuilder();
        sb.AppendLine(s.SignalName);
        sb.AppendLine($"Kind: {b.Kind}   ·   {b.Size} bits @ byte {b.StartByte} / bit {b.StartBit}");
        sb.AppendLine($"Frame: {s.FrameName} [{s.FrameType}]   PDU: {s.PduName}");
        if (s.Unit.Length + s.Min.Length + s.Max.Length + s.Resolution.Length > 0)
            sb.AppendLine($"Unit {Dash(s.Unit)} · Min {Dash(s.Min)} · Max {Dash(s.Max)} · Res {Dash(s.Resolution)}");
        if (s.Coding.Length > 0) sb.AppendLine($"Coding: {s.Coding}");
        var tx = string.Join(", ", s.Transmitters);
        var rx = string.Join(", ", s.EcuTxRx.Where(kv => kv.Value.Contains('R', StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key));
        sb.AppendLine($"Tx: {Dash(tx)}   Rx: {Dash(rx)}");
        sb.AppendLine($"Functional: {(s.Functional ? "yes" : "no")}");

        if (_data is not null && _data.IsrsByParameter.TryGetValue(s.SignalName, out var isrs) && isrs.Count > 0)
        {
            int active = isrs.Count(a => a.IsActive);
            sb.AppendLine($"ISRs: {isrs.Count} ({active} active)");
            foreach (var a in isrs.Take(8))
                sb.AppendLine($"  • {a.IsrNumber}  [{Status1(a.LatestStatus)}]  {a.Transmitter}→{a.Receiver}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Dash(string v) => v.Length > 0 ? v : "—";
    private static string Status1(string s) => s.Trim().ToUpperInvariant() switch
    { "X" => "Active", "A" => "Abandoned", "R" => "Refused", _ => "—" };
}
