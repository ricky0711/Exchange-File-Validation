using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Services;

/// <summary>
/// Assigns L0/L1/L2/L2.1/L3 to each demand, mirroring the macro logic:
///   L0  = ISR n° already exists in ISR-applied
///   L1  = Signal + Tx + Rx all match an applied ISR
///   L2  = Signal + Tx match (Rx differs)
///   L2.1= signal exists in a *second* architecture's Message List
///   L3  = none of the above (genuinely new)
///
/// VERIFY THE MATCH FIELDS: this maps demand.ParameterProposal→applied.Parameter,
/// demand.Emitter→applied.Transmitter, demand.Receiver→applied.Receiver. If your
/// applied-ISR sheet keys off different columns (e.g. PREEvision Tx/Rx, or ECU codes
/// via Dico), adjust BuildKey / the field picks below — it's a one-spot change.
/// </summary>
public sealed class LevelAssignmentService
{
    private static string Key(params string[] parts) =>
        string.Join("|", parts.Select(p => (p ?? "").Trim().ToUpperInvariant()));

    public void Assign(IEnumerable<IsrDemand> demands, ReferenceData data, HashSet<string>? secondArchSignals = null)
    {
        // Only confirmed/applied rows count (macro checked last release col = "x" or "A").
        var applied = data.AppliedIsrs
            .Where(a => a.LatestStatus.Equals("x", StringComparison.OrdinalIgnoreCase)
                     || a.LatestStatus.Equals("A", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (applied.Count == 0) applied = data.AppliedIsrs;   // fallback if no release col

        var isrNumbers = new HashSet<string>(applied.Select(a => a.IsrNumber.Trim()), StringComparer.OrdinalIgnoreCase);
        var l1 = new HashSet<string>(applied.Select(a => Key(a.Parameter, a.Transmitter, a.Receiver)));
        var l2 = new HashSet<string>(applied.Select(a => Key(a.Parameter, a.Transmitter)));

        foreach (var d in demands)
        {
            d.Results.RemoveAll(r => r.Rule == "Level");

            if (d.IsrNumber.Length > 0 && isrNumbers.Contains(d.IsrNumber.Trim()))
            {
                Set(d, IsrLevel.Level0, "ISR n° already present in ISR-applied.");
            }
            else if (l1.Contains(Key(d.ParameterProposal, d.Emitter, d.Receiver)))
            {
                Set(d, IsrLevel.Level1, "Signal + Tx + Rx matched an applied ISR.");
            }
            else if (l2.Contains(Key(d.ParameterProposal, d.Emitter)))
            {
                Set(d, IsrLevel.Level2, "Signal + Tx matched (Rx differs).");
            }
            else if (secondArchSignals is not null && secondArchSignals.Contains(d.ParameterProposal))
            {
                Set(d, IsrLevel.Level2_1, "Signal present in the second architecture's Message List.");
            }
            else
            {
                d.Level = IsrLevel.Level3;
                var note = secondArchSignals is null
                    ? "No applied match. Load a 2nd-architecture Message List to confirm vs Level 2.1."
                    : "New — not in ISR-applied and not in the 2nd architecture.";
                Set(d, IsrLevel.Level3, note);
            }
        }
    }

    private static void Set(IsrDemand d, IsrLevel level, string note)
    {
        d.Level = level;
        var sev = level == IsrLevel.Level3 ? Severity.Warning : Severity.Info;
        d.Results.Add(new ValidationResult("Level", sev, note));
    }

    public static (int L0, int L1, int L2, int L21, int L3, int Unassigned) Counts(IEnumerable<IsrDemand> demands)
    {
        int l0 = 0, l1 = 0, l2 = 0, l21 = 0, l3 = 0, un = 0;
        foreach (var d in demands)
            switch (d.Level)
            {
                case IsrLevel.Level0: l0++; break;
                case IsrLevel.Level1: l1++; break;
                case IsrLevel.Level2: l2++; break;
                case IsrLevel.Level2_1: l21++; break;
                case IsrLevel.Level3: l3++; break;
                default: un++; break;
            }
        return (l0, l1, l2, l21, l3, un);
    }
}
