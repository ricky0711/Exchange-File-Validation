using System.Text.RegularExpressions;

namespace ExchangeFileValidator.Services;

/// <summary>
/// Splits the ISR's LogicalData / AnalogData cells (bracketed "[Key: Value]" tokens) into the
/// signal properties the AT validation compares: unit, min, max, resolution, coding, meaning, states.
/// AnalogData → continuous signal (unit/min/max/resolution). LogicalData → discrete states (Etat_N).
/// </summary>
public sealed class SignalDataParser
{
    public sealed class Parsed
    {
        public string Unit = "";
        public string Min = "";
        public string Max = "";
        public string Resolution = "";
        public string Offset = "";
        public int? States;          // logical
        public string Coding = "";   // joined Etat codes
        public string Meaning = "";  // joined Etat meanings
        public bool HasAnalog;
        public bool HasLogical;
    }

    private static readonly Regex Bracketed = new(@"\[\s*([^:\]]+?)\s*:\s*([^\]]*?)\s*\]", RegexOptions.Compiled);

    private static string NormKey(string k) =>
        new string((k ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public Parsed Parse(string logicalData, string analogData)
    {
        var p = new Parsed();

        // --- Analog: continuous ---
        if (!string.IsNullOrWhiteSpace(analogData))
        {
            p.HasAnalog = true;
            foreach (Match m in Bracketed.Matches(analogData))
            {
                var key = NormKey(m.Groups[1].Value);
                var val = m.Groups[2].Value.Trim();
                if (key is "unit" or "unite" or "unit") p.Unit = val;
                else if (key.Contains("valeurmin") || key == "min" || key == "minimum") p.Min = val;
                else if (key.Contains("valeurmax") || key == "max" || key == "maximum") p.Max = val;
                else if (key.StartsWith("resolution") || key == "res") p.Resolution = val;
                else if (key.StartsWith("offset")) p.Offset = val;
            }
        }

        // --- Logical: discrete states ---
        if (!string.IsNullOrWhiteSpace(logicalData))
        {
            p.HasLogical = true;
            var codes = new List<string>();
            var meanings = new List<string>();
            foreach (Match m in Bracketed.Matches(logicalData))
            {
                var key = m.Groups[1].Value.Trim();
                var val = m.Groups[2].Value.Trim();
                if (key.StartsWith("Etat", StringComparison.OrdinalIgnoreCase) || key.StartsWith("State", StringComparison.OrdinalIgnoreCase))
                {
                    codes.Add(key);
                    meanings.Add(val);
                }
            }
            if (codes.Count > 0)
            {
                p.States = codes.Count;
                p.Coding = string.Join("\n", codes);
                p.Meaning = string.Join("\n", meanings);
            }
        }

        return p;
    }
}
