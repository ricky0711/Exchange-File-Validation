using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Services;

/// <summary>
/// AT validation = ISR-online vs Message-Set. Two flavours:
///  • Signal properties — unit/min/max/resolution (AnalogData) and coding/meaning/size (LogicalData),
///    parsed from the ISR and compared against the Message-Set definition.
///  • Other Requirements — Tx/Rx/UnavailableValue/NetworkPath parsed from the OtherRequirements cell.
/// Only signals that exist in the Message Set are compared.
/// </summary>
public sealed class PropertyComparisonService
{
    private static readonly Dictionary<string, string> EcuMap = new(StringComparer.OrdinalIgnoreCase)
    { ["PIU_Mst"] = "PIU_MASTER", ["PIU_Hood"] = "PIU_HOOD", ["PIU_Sub"] = "PIU_SUB" };
    private static string Ecu(string n) => EcuMap.TryGetValue((n ?? "").Trim(), out var v) ? v : (n ?? "").Trim();

    private static bool Same(string a, string b)
    {
        a = (a ?? "").Trim(); b = (b ?? "").Trim();
        if (a.Length == 0 || b.Length == 0) return true;                 // can't compare → don't flag
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        if (double.TryParse(a, out var da) && double.TryParse(b, out var db)) return Math.Abs(da - db) < 1e-9;
        return false;
    }

    private readonly SignalDataParser _parser = new();

    /// <summary>Point 16 — ISR signal properties (from Logical/Analog data) vs Message Set.</summary>
    public List<CompareRow> CompareSignalProperties(IReadOnlyList<IsrDemand> demands, ReferenceData data, bool mismatchesOnly)
    {
        var rows = new List<CompareRow>();
        foreach (var d in demands)
        {
            if (string.IsNullOrWhiteSpace(d.ParameterProposal)) continue;
            var sig = d.MatchedDef; if (sig is null) continue;   // resolved frame instance (Part A)
            var p = _parser.Parse(d.LogicalData, d.AnalogData);

            if (p.HasAnalog)
            {
                Add(rows, d, "Unit", p.Unit, sig.Unit, mismatchesOnly);
                Add(rows, d, "Min", p.Min, sig.Min, mismatchesOnly);
                Add(rows, d, "Max", p.Max, sig.Max, mismatchesOnly);
                Add(rows, d, "Resolution", p.Resolution, sig.Resolution, mismatchesOnly);
            }
            if (p.HasLogical)
            {
                string isrBits = p.States is int st && st >= 2 ? ((int)Math.Ceiling(Math.Log2(st))).ToString() : "";
                Add(rows, d, "Size (bits)", isrBits, sig.SignalSizeBits?.ToString() ?? "", mismatchesOnly);
                Add(rows, d, "States", p.States?.ToString() ?? "",
                    sig.Meaning.Split('\n').Count(x => x.Trim().Length > 0).ToString(), mismatchesOnly);
                Add(rows, d, "Meaning", p.Meaning, sig.Meaning, mismatchesOnly);
            }
        }
        return rows;
    }

    /// <summary>Point 17 — OtherRequirements (Tx/Rx/UV/NetworkPath) vs the ISR + Message Set.</summary>
    public List<CompareRow> CompareOtherRequirements(IReadOnlyList<IsrDemand> demands, ReferenceData data, bool mismatchesOnly)
    {
        var rows = new List<CompareRow>();
        foreach (var d in demands)
        {
            if (string.IsNullOrWhiteSpace(d.OtherRequirements)) continue;
            var sig = d.MatchedDef;   // resolved frame instance (Part A)

            if (d.ReqTx.Length > 0)
            {
                var msgTx = sig is null ? d.Emitter : string.Join(", ", sig.Transmitters);
                Add(rows, d, "Tx", Ecu(d.ReqTx), msgTx.Length == 0 ? d.Emitter : msgTx, mismatchesOnly, exact: true);
            }
            if (d.ReqRx.Length > 0)
                Add(rows, d, "Rx", Ecu(d.ReqRx), d.Receiver, mismatchesOnly, exact: true);
            if (d.ReqUnavailableValue.Length > 0)
                Add(rows, d, "Unavailable value", d.ReqUnavailableValue, sig?.Coding ?? "", mismatchesOnly);
            if (d.ReqNetworkPath.Length > 0)
            {
                var route = data.Routes.FirstOrDefault(r =>
                    r.Transmitter.Equals(d.Emitter, StringComparison.OrdinalIgnoreCase) &&
                    r.Receiver.Equals(d.Receiver, StringComparison.OrdinalIgnoreCase));
                Add(rows, d, "Network path", d.ReqNetworkPath, route?.SynthesisPath ?? "", mismatchesOnly);
            }
        }
        return rows;
    }

    private static void Add(List<CompareRow> rows, IsrDemand d, string prop, string isr, string msg, bool mismatchesOnly, bool exact = false)
    {
        bool match = exact
            ? (isr ?? "").Trim().Equals((msg ?? "").Trim(), StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(msg)
            : Same(isr, msg);
        if (mismatchesOnly && match) return;
        rows.Add(new CompareRow(d.IsrNumber, d.ParameterProposal, prop, isr, msg, match));
    }
}
