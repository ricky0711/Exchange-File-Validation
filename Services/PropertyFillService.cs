using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Services;

/// <summary>
/// Phase 3 — fills each demand's frame/PDU/signal definition from the Message List
/// (or the 2nd architecture for L2.1), and reports whether the target frame already
/// carries a CRC / Clock signal.
///
/// NOTE: full CRC/CLK *need* (the macro's Tx/Rx-aware "new CRC required" decision)
/// also depends on the per-ECU T/R columns of the Message List, which Phase 0 does
/// not load yet. Here we report frame-level CRC/CLK presence; wiring the ECU columns
/// upgrades this to the full decision later.
/// </summary>
public sealed class PropertyFillService
{
    public void Fill(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        var frameHasCrcClk = BuildFrameCrcClk(data);

        foreach (var d in demands)
        {
            d.FilledFrame = d.FilledPdu = d.FilledFrameId = d.FilledValueType = "";
            d.FilledUnit = d.FilledMin = d.FilledMax = d.FillSource = d.FillStatus = d.CrcNote = "";
            d.CrcStatus = d.ClkStatus = "";
            d.FilledBits = null; d.HasCrcOnFrame = d.HasClkOnFrame = false;

            var name = d.ParameterProposal ?? "";
            SignalDef? def = null;
            string source = "";

            if (name.Length > 0 && data.SignalByName.TryGetValue(name, out var s1))
            {
                def = s1; source = "Message List";
            }
            else if (name.Length > 0 && data.SecondArchByName is not null
                     && data.SecondArchByName.TryGetValue(name, out var s2))
            {
                def = s2; source = "2nd architecture";
            }

            if (def is null)
            {
                d.FillStatus = d.Level == IsrLevel.Level2_1 && data.SecondArchByName is null
                    ? "Load 2nd architecture to fill (L2.1)."
                    : "New signal — no definition found. Define in PREEvision.";
                continue;
            }

            d.FilledFrame = def.FrameName;
            d.FilledFrameId = def.FrameIdHex;
            d.FilledPdu = def.PduName;
            d.FilledBits = def.SignalSizeBits;
            d.FilledValueType = def.ValueType;
            d.FilledUnit = def.Unit;
            d.FilledMin = def.Min;
            d.FilledMax = def.Max;
            d.FillSource = source;

            frameHasCrcClk.TryGetValue(def.FrameName, out var cc);
            d.HasCrcOnFrame = cc.crc is not null;
            d.HasClkOnFrame = cc.clk is not null;
            var (crcNote, crcState) = Reuse(cc.crc, "CRC", d);
            var (clkNote, clkState) = Reuse(cc.clk, "Clock", d);
            d.CrcNote = crcNote + "; " + clkNote;
            d.CrcStatus = crcState; d.ClkStatus = clkState;
            d.FillStatus = $"Filled from {source}. {d.CrcNote}";
        }
    }

    /// <summary>Map frame name -> (CRC signal def, Clock signal def) if present, across both archs.</summary>
    private static Dictionary<string, (SignalDef? crc, SignalDef? clk)> BuildFrameCrcClk(ReferenceData data)
    {
        var map = new Dictionary<string, (SignalDef? crc, SignalDef? clk)>(StringComparer.OrdinalIgnoreCase);
        void Scan(IEnumerable<SignalDef> signals)
        {
            foreach (var s in signals)
            {
                if (string.IsNullOrEmpty(s.FrameName)) continue;
                map.TryGetValue(s.FrameName, out var cur);
                map[s.FrameName] = (cur.crc ?? (s.IsCrc ? s : null), cur.clk ?? (s.IsClock ? s : null));
            }
        }
        Scan(data.Signals);
        if (data.SecondArchByName is not null) Scan(data.SecondArchByName.Values);
        return map;
    }

    /// <summary>Macro AddISRClockAndCRC logic: an existing CRC/Clock is reusable only if it already
    /// has T at the demand's emitter and R at the receiver in the Message List node columns.</summary>
    private static (string note, string state) Reuse(SignalDef? sig, string kind, IsrDemand d)
    {
        if (sig is null) return ($"no {kind} on frame - new {kind} likely needed", "new");
        bool know = sig.EcuTxRx.Count > 0;
        if (!know) return ($"{kind} present (Tx/Rx coverage unknown)", "present");
        bool tOk = sig.EcuTxRx.TryGetValue(d.Emitter ?? "", out var tv) && tv.Contains('T', StringComparison.OrdinalIgnoreCase);
        bool rOk = sig.EcuTxRx.TryGetValue(d.Receiver ?? "", out var rv) && rv.Contains('R', StringComparison.OrdinalIgnoreCase);
        if (tOk && rOk) return ($"{kind} reusable (covers Tx+Rx)", "reuse");
        var miss = (!tOk ? "T@" + d.Emitter + " " : "") + (!rOk ? "R@" + d.Receiver : "");
        return ($"{kind} present but missing {miss.Trim()} - new {kind} may be needed", "new");
    }

    public static (int filled, int pending, int newSig) Counts(IEnumerable<IsrDemand> demands)
    {
        int filled = 0, pending = 0, newSig = 0;
        foreach (var d in demands)
        {
            if (d.FillSource.Length > 0) filled++;
            else if (d.FillStatus.StartsWith("Load 2nd")) pending++;
            else newSig++;
        }
        return (filled, pending, newSig);
    }
}
