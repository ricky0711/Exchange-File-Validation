using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Services;

/// <summary>
/// Builds the expandable per-ISR detail (section C): a side-by-side ISR(online)-vs-AT(Message Set)
/// property comparison with per-row match flags, plus the CRC / Clock reuse badges.
/// Pure: takes a demand + reference data, fills <see cref="IsrDemand.Detail"/>.
/// </summary>
public sealed class IsrDetailBuilder
{
    private readonly SignalDataParser _parser = new();

    private static readonly Dictionary<string, string> EcuMap = new(StringComparer.OrdinalIgnoreCase)
    { ["PIU_Mst"] = "PIU_MASTER", ["PIU_Hood"] = "PIU_HOOD", ["PIU_Sub"] = "PIU_SUB" };
    private static string Ecu(string n) => EcuMap.TryGetValue((n ?? "").Trim(), out var v) ? v : (n ?? "").Trim();

    /// <summary>Blank-tolerant, numeric-aware equality (mirrors PropertyComparisonService.Same).</summary>
    private static bool Same(string a, string b)
    {
        a = (a ?? "").Trim(); b = (b ?? "").Trim();
        if (a.Length == 0 || b.Length == 0) return true;            // nothing to compare → not a mismatch
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        if (double.TryParse(a, out var da) && double.TryParse(b, out var db)) return Math.Abs(da - db) < 1e-9;
        return false;
    }

    public void Build(IsrDemand d, ReferenceData data)
    {
        // Part A: the matched frame instance was resolved centrally (FrameMatchService).
        SignalDef? sig = d.MatchedDef;

        var p = _parser.Parse(d.LogicalData, d.AnalogData);
        var detail = new IsrDetail
        {
            HasAt = sig is not null,
            CrcBadge = Badge("CRC", d.CrcStatus, d.HasCrcOnFrame),
            CrcState = d.CrcStatus,
            ClkBadge = Badge("Clock", d.ClkStatus, d.HasClkOnFrame),
            ClkState = d.ClkStatus,
        };

        string atTx = sig is null ? "" : string.Join(", ", sig.Transmitters);
        string atRx = sig is null ? "" : string.Join(", ",
            sig.EcuTxRx.Where(kv => kv.Value.Contains('R', StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key));
        string isrBits = p.States is int st && st >= 2 ? ((int)Math.Ceiling(Math.Log2(st))).ToString() : "";

        // info=true → shown but never flagged red (text whose ISR/AT forms aren't directly comparable).
        void Row(string prop, string isr, string at, bool info = false)
        {
            // Skip rows where both sides are blank (nothing to show).
            if (string.IsNullOrWhiteSpace(isr) && string.IsNullOrWhiteSpace(at)) return;
            detail.Props.Add(new CompareRow(d.IsrNumber, d.ParameterProposal, prop, isr, at, info || Same(isr, at)));
        }

        Row("Transmitter (Tx)", Ecu(d.ReqTx.Length > 0 ? d.ReqTx : d.Emitter), atTx.Length > 0 ? atTx : "");
        Row("Receiver (Rx)", Ecu(d.ReqRx.Length > 0 ? d.ReqRx : d.Receiver), atRx);
        Row("Frame", d.Frame, sig?.FrameName ?? "");
        Row("PDU", "", sig?.PduName ?? "");
        Row("Size (bits)", isrBits, sig?.SignalSizeBits?.ToString() ?? "");
        Row("Unit", p.Unit, sig?.Unit ?? "");
        Row("Min", p.Min, sig?.Min ?? "");
        Row("Max", p.Max, sig?.Max ?? "");
        Row("Resolution", p.Resolution, sig?.Resolution ?? "");
        Row("Coding", p.Coding, sig?.Coding ?? "", info: true);     // logical states vs binary coding — not directly comparable
        Row("Meaning", p.Meaning, sig?.Meaning ?? "", info: true);  // multi-line free text — show, don't flag
        Row("Period", "", sig?.Period ?? "");
        Row("Functional (AT)", "", sig is null ? "" : (sig.Functional ? "functional" : "non-functional"), info: true);
        Row("Unavailable value", d.ReqUnavailableValue, "");
        Row("Network path", d.ReqNetworkPath, "");
        Row("Change mgmt n°", d.ReqChangeMgmt, "");

        d.Detail = detail;

        // Provisional "Ready to import" gate: a demand is ready unless it has a blocking Error finding.
        // (Real Alliance gate is an explicit column — reconcile when a sample export is available.)
        d.ImportStatus = d.WorstSeverity == Severity.Error ? "Blocked (errors)" : "Ready to import";
    }

    private static string Badge(string kind, string state, bool onFrame) => state switch
    {
        "reuse" => $"{kind}: reuse existing",
        "new" => onFrame ? $"{kind}: new needed" : $"{kind}: none on frame — new needed",
        "present" => $"{kind}: present (coverage unknown)",
        "none" => $"{kind}: not required (no ASIL)",
        "undetermined" => $"{kind}: ASIL undetermined",
        _ => $"{kind}: n/a (new signal)",
    };
}
