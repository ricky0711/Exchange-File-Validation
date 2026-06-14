using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ExchangeFileValidator.Models;
using ExchangeFileValidator.Services;

namespace ExchangeFileValidator.ViewModels;

/// <summary>Base lazy tree node — children are built only on first expand (Feature 1).</summary>
public abstract partial class ExplorerNode : ObservableObject
{
    private bool _loaded;
    protected readonly ReferenceData Data;

    protected ExplorerNode(ReferenceData data, bool hasChildren)
    {
        Data = data;
        if (hasChildren) Children.Add(new LoadingNode(data));   // dummy so the expander shows
    }

    public ObservableCollection<ExplorerNode> Children { get; } = new();
    [ObservableProperty] private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_loaded)
        {
            _loaded = true;
            Children.Clear();
            LoadChildren(Children);
        }
    }

    protected virtual void LoadChildren(ObservableCollection<ExplorerNode> into) { }
    public virtual string SearchText => "";
}

public sealed class LoadingNode : ExplorerNode
{
    public LoadingNode(ReferenceData d) : base(d, false) { }
}

/// <summary>PDU root → the frames that carry it.</summary>
public sealed class PduNode : ExplorerNode
{
    public PduNode(ReferenceData d, string pdu)
        : base(d, d.FramesByPdu.TryGetValue(pdu, out var f) && f.Count > 0) => Name = pdu;

    public string Name { get; }
    public int FrameCount => Data.FramesByPdu.TryGetValue(Name, out var f) ? f.Count : 0;
    public string CountText => $"{FrameCount} frame{(FrameCount == 1 ? "" : "s")}";
    public override string SearchText => Name;

    protected override void LoadChildren(ObservableCollection<ExplorerNode> into)
    {
        if (!Data.FramesByPdu.TryGetValue(Name, out var frames)) return;
        foreach (var fr in frames) into.Add(new FrameNode(Data, fr));
    }
}

/// <summary>Frame → the signals mapped into it.</summary>
public sealed class FrameNode : ExplorerNode
{
    public FrameNode(ReferenceData d, string frame)
        : base(d, d.SignalsByFrame.TryGetValue(frame, out var s) && s.Count > 0) => Name = frame;

    public string Name { get; }
    private List<SignalDef> Sigs => Data.SignalsByFrame.TryGetValue(Name, out var s) ? s : new();
    private SignalDef? Rep => Sigs.Count > 0 ? Sigs[0] : null;

    public string FrameId => Rep?.FrameIdHex ?? "";
    public string FrameType => Rep?.FrameType ?? "";
    public string Tx => ResolveTx();
    public int SignalCount => Sigs.Count;
    public string CountText => $"{SignalCount} signal{(SignalCount == 1 ? "" : "s")}";
    public bool IsContainer => Data.ContainersByFrame.ContainsKey(Name) || (Rep?.IsContainerFrame ?? false);
    public string Meta => $"ID {(FrameId.Length > 0 ? FrameId : "—")} · {(FrameType.Length > 0 ? FrameType : "—")} · Tx {(Tx.Length > 0 ? Tx : "—")}";
    public override string SearchText => Name;

    // Assumption: if the frame is a container, its transmitter is the container master (Construction 'Tx unit');
    // otherwise the union of T-column ECUs across its signals.
    private string ResolveTx()
    {
        if (Data.ContainersByFrame.TryGetValue(Name, out var c) && c.TxUnit.Length > 0) return c.TxUnit;
        var txs = Sigs.SelectMany(s => s.Transmitters).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return txs.Count == 0 ? "" : string.Join(", ", txs);
    }

    protected override void LoadChildren(ObservableCollection<ExplorerNode> into)
    {
        foreach (var s in Sigs) into.Add(new SignalNode(Data, s));
    }
}

/// <summary>Signal → its ISRs (from ISR-Applied).</summary>
public sealed class SignalNode : ExplorerNode
{
    private readonly SignalDef _sig;

    public SignalNode(ReferenceData d, SignalDef sig)
        : base(d, d.IsrsByParameter.TryGetValue(sig.SignalName, out var i) && i.Count > 0) => _sig = sig;

    public string Name => _sig.SignalName;
    public bool Functional => _sig.Functional;
    public bool IsStructural => _sig.IsStructural;
    public string Position => (_sig.BytePosition.Length > 0 || _sig.BitPosition.Length > 0)
        ? $"byte {Or(_sig.BytePosition)}/bit {Or(_sig.BitPosition)}" : "";
    private List<AppliedIsr> Isrs => Data.IsrsByParameter.TryGetValue(_sig.SignalName, out var i) ? i : new();
    public int IsrCount => Isrs.Count;
    public int ActiveIsrCount => Isrs.Count(a => a.IsActive);
    public string CountText => IsrCount > 0
        ? $"{IsrCount} ISR{(IsrCount == 1 ? "" : "s")} ({ActiveIsrCount} active)"
        : (_sig.IsStructural ? "structural" : "no ISR");
    public override string SearchText => Name;

    protected override void LoadChildren(ObservableCollection<ExplorerNode> into)
    {
        foreach (var a in Isrs) into.Add(new IsrNode(Data, a));
    }

    private static string Or(string v) => v.Length > 0 ? v : "?";
}

/// <summary>ISR leaf.</summary>
public sealed class IsrNode : ExplorerNode
{
    private readonly AppliedIsr _a;
    public IsrNode(ReferenceData d, AppliedIsr a) : base(d, false) => _a = a;

    public string IsrNumber => _a.IsrNumber;
    public string FrameName => _a.Frame;
    public string Status => _a.LatestStatus.Trim().ToUpperInvariant() switch
    {
        "X" => "Active",
        "A" => "Abandoned",
        "R" => "Refused",
        _ => "—"
    };
    public string Tx => _a.Transmitter;
    public string Rx => _a.Receiver;
    public string Route => $"{(Tx.Length > 0 ? Tx : "?")} → {(Rx.Length > 0 ? Rx : "?")}";
    public override string SearchText => IsrNumber;
}
