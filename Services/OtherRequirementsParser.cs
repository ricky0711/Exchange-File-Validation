using System.Text.RegularExpressions;
using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Services;

/// <summary>
/// Parses the OtherRequirements cell, which holds keyed lines like:
///   Tx: PIU_Mst ; Rx: PCM ; NetworkPath: ... ; UnavailableValue: 0b11 ; ChangeManagementNumber: ...
/// (separators may be newlines, ';', or numbered "1. 2." prefixes — all tolerated).
/// </summary>
public static class OtherRequirementsParser
{
    private static readonly string[] Keys =
        { "Tx", "Rx", "NetworkPath", "UnavailableValue", "ChangeManagementNumber", "OtherRequirements" };

    public static void Apply(IsrDemand d)
    {
        var map = Parse(d.OtherRequirements);
        map.TryGetValue("Tx", out var tx); d.ReqTx = tx ?? "";
        map.TryGetValue("Rx", out var rx); d.ReqRx = rx ?? "";
        map.TryGetValue("NetworkPath", out var np); d.ReqNetworkPath = np ?? "";
        map.TryGetValue("UnavailableValue", out var uv); d.ReqUnavailableValue = uv ?? "";
        map.TryGetValue("ChangeManagementNumber", out var cm); d.ReqChangeMgmt = cm ?? "";
    }

    public static Dictionary<string, string> Parse(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return result;

        // build a regex that finds "Key:" and captures up to the next key or end
        string keyAlt = string.Join("|", Keys.Select(Regex.Escape));
        var rx = new Regex($@"(?<key>{keyAlt})\s*:\s*(?<val>.*?)(?=(?:[;\n\r]\s*)?(?:\d+\.\s*)?(?:{keyAlt})\s*:|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match m in rx.Matches(raw))
        {
            var key = m.Groups["key"].Value.Trim();
            var val = m.Groups["val"].Value.Trim().Trim(';', '.', ',').Trim();
            if (key.Equals("OtherRequirements", StringComparison.OrdinalIgnoreCase)) continue;
            if (!result.ContainsKey(key)) result[key] = val;
        }
        return result;
    }
}
