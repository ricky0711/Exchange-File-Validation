using System.Text.RegularExpressions;

namespace ExchangeFileValidator.Services;

/// <summary>Whether an ASIL is requested for a demand (drives E2E CRC+Clock and secure-container decisions).</summary>
public enum AsilState { None, Requested, Undetermined }

/// <summary>
/// Part D — reads the two ASIL columns (LossLinkageASIL / CorruptDataASIL). The tool only needs to know
/// whether an ASIL is requested at all: any column carrying an ASIL value ⇒ requested; an empty column ⇒
/// undetermined (cannot decide — flag it); explicit QM / no-ASIL ⇒ none.
/// </summary>
public static class AsilDetector
{
    private static readonly Regex AsilValue = new(@"ASIL\s*[ABCD]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool HasAsil(string v)
    {
        v = (v ?? "").Trim();
        if (v.Length == 0) return false;
        if (v.Contains("QM", StringComparison.OrdinalIgnoreCase)) return false;   // QM / QM(B) = no ASIL
        if (AsilValue.IsMatch(v)) return true;
        return v.Length == 1 && "ABCD".Contains(char.ToUpperInvariant(v[0]));
    }

    private static bool IsEmpty(string v) => string.IsNullOrWhiteSpace(v);

    public static AsilState Detect(string lossLinkage, string corruptData)
    {
        if (HasAsil(lossLinkage) || HasAsil(corruptData)) return AsilState.Requested;
        if (IsEmpty(lossLinkage) || IsEmpty(corruptData)) return AsilState.Undetermined;
        return AsilState.None;
    }
}
