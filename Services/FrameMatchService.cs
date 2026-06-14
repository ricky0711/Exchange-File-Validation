using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Services;

/// <summary>Result of resolving a demand's proposed signal to a concrete frame mapping.</summary>
public sealed record SignalMatch(SignalDef? Def, IReadOnlyList<SignalDef> Candidates, string Source)
{
    /// <summary>The signal maps into more than one frame (the engineer should confirm which).</summary>
    public bool Ambiguous => Candidates.Count > 1;
}

/// <summary>
/// Part A — a signal appears once per frame it is mapped into (≈36% of FACE signals map into 2–4 frames:
/// classic CAN / CAN-FD container / secured container). This picks the correct <see cref="SignalDef"/>
/// instance for a demand, so every consumer (fill, validation, compare, route) resolves the same frame.
/// Priority: (a) explicit frame named on the demand → (b) FD-vs-HS preference → (c) Tx/Rx node coverage →
/// (d) first remaining. Falls back to the single row when only one mapping exists.
/// </summary>
public sealed class FrameMatchService
{
    /// <summary>Resolve and cache MatchedDef / MatchedFrames / MatchSource on every demand.</summary>
    public void Apply(IEnumerable<IsrDemand> demands, ReferenceData data)
    {
        foreach (var d in demands)
        {
            var m = Resolve(d, data);
            d.MatchedDef = m.Def;
            d.MatchedFrames = m.Candidates.ToList();
            d.MatchSource = m.Source;
        }
    }

    public static SignalMatch Resolve(IsrDemand d, ReferenceData data)
    {
        var name = (d.ParameterProposal ?? "").Trim();
        if (name.Length == 0) return new SignalMatch(null, Array.Empty<SignalDef>(), "");

        if (data.SignalMappingsByName.TryGetValue(name, out var all) && all.Count > 0)
            return new SignalMatch(Pick(d, all), all, "Message List");

        if (data.SecondArchByName is not null && data.SecondArchByName.TryGetValue(name, out var s2))
            return new SignalMatch(s2, new[] { s2 }, "2nd architecture");

        return new SignalMatch(null, Array.Empty<SignalDef>(), "");
    }

    private static SignalDef Pick(IsrDemand d, IReadOnlyList<SignalDef> all)
    {
        if (all.Count == 1) return all[0];
        IEnumerable<SignalDef> c = all;

        // (a) explicit frame named on the demand (matches Frame Name or its container)
        var frame = (d.Frame ?? "").Trim();
        if (frame.Length > 0)
        {
            var byFrame = all.Where(s =>
                s.FrameName.Equals(frame, StringComparison.OrdinalIgnoreCase) ||
                s.FrameContainer.Equals(frame, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byFrame.Count == 1) return byFrame[0];
            if (byFrame.Count > 1) c = byFrame;
        }

        // (b) FD vs HS preference from the demand's media / network type
        bool wantFd = (d.NetworkType + " " + d.MediaType).Contains("FD", StringComparison.OrdinalIgnoreCase);
        var byFd = c.Where(s => s.IsFd == wantFd).ToList();
        if (byFd.Count >= 1) c = byFd;

        // (c) Rx coverage — the requested receiver may be marked R on only ONE frame instance; prefer it.
        var byRx = c.Where(s => s.EcuTxRx.TryGetValue(d.Receiver ?? "", out var rv) && rv.Contains('R', StringComparison.OrdinalIgnoreCase)).ToList();
        if (byRx.Count >= 1) c = byRx;

        // (c2) then prefer the instance whose emitter is marked T (the actual frame transmitter / container master)
        var byTx = c.Where(s => s.EcuTxRx.TryGetValue(d.Emitter ?? "", out var tv) && tv.Contains('T', StringComparison.OrdinalIgnoreCase)).ToList();
        if (byTx.Count >= 1) c = byTx;

        // (d) first remaining candidate
        return c.First();
    }
}
