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

    /// <summary>
    /// Configurable AnalogData key aliases (normalized: lower-case, letters+digits only). Tune here if a
    /// real Exchange File uses different labels — matching is alias-equality / prefix / substring, so e.g.
    /// "Valeur_min (Dec)" → "valeurmindec" still resolves to Min. (LogicalData "[Etat_N: …]" is confirmed.)
    /// </summary>
    public static readonly (string field, string[] aliases)[] AnalogKeyAliases =
    {
        ("unit",       new[] { "unit", "unite", "unity", "unitdec" }),
        ("min",        new[] { "valeurmin", "valmin", "minimum", "min", "minvalue", "borneinf" }),
        ("max",        new[] { "valeurmax", "valmax", "maximum", "max", "maxvalue", "bornesup" }),
        ("resolution", new[] { "resolution", "res", "pas", "step", "quantum", "lsb" }),
        ("offset",     new[] { "offset", "decalage" }),
    };

    private static string? MatchField(string normKey)
    {
        foreach (var (field, aliases) in AnalogKeyAliases)
            foreach (var a in aliases)
                if (normKey == a || normKey.StartsWith(a, StringComparison.Ordinal) || normKey.Contains(a, StringComparison.Ordinal))
                    return field;
        return null;
    }

    private static void ApplyAnalogField(Parsed p, string key, string val)
    {
        switch (MatchField(key))
        {
            case "unit": p.Unit = val; break;
            case "min": p.Min = val; break;
            case "max": p.Max = val; break;
            case "resolution": p.Resolution = val; break;
            case "offset": p.Offset = val; break;
        }
    }

    public Parsed Parse(string logicalData, string analogData)
    {
        var p = new Parsed();

        // --- Analog: continuous ---
        if (!string.IsNullOrWhiteSpace(analogData))
        {
            p.HasAnalog = true;
            var matches = Bracketed.Matches(analogData);
            if (matches.Count > 0)
            {
                foreach (Match m in matches)
                {
                    var key = NormKey(m.Groups[1].Value);
                    var val = m.Groups[2].Value.Trim();
                    ApplyAnalogField(p, key, val);
                }
            }
            else
            {
                var pairs = analogData.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var pair in pairs)
                {
                    var parts = pair.Split(new[] { '=', ':' }, 2);
                    if (parts.Length == 2)
                    {
                        var key = NormKey(parts[0]);
                        var val = parts[1].Trim();
                        ApplyAnalogField(p, key, val);
                    }
                }
            }
        }

        // --- Logical: discrete states ---
        if (!string.IsNullOrWhiteSpace(logicalData))
        {
            p.HasLogical = true;
            var codes = new List<string>();
            var meanings = new List<string>();
            var matches = Bracketed.Matches(logicalData);
            if (matches.Count > 0)
            {
                foreach (Match m in matches)
                {
                    var key = m.Groups[1].Value.Trim();
                    var val = m.Groups[2].Value.Trim();
                    if (key.StartsWith("Etat", StringComparison.OrdinalIgnoreCase) || key.StartsWith("State", StringComparison.OrdinalIgnoreCase))
                    {
                        codes.Add(key);
                        meanings.Add(val);
                    }
                }
            }
            else
            {
                var pairs = logicalData.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var pair in pairs)
                {
                    var parts = pair.Split(new[] { '=', ':' }, 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var val = parts[1].Trim();
                        if (key.StartsWith("Etat", StringComparison.OrdinalIgnoreCase) || key.StartsWith("State", StringComparison.OrdinalIgnoreCase))
                        {
                            codes.Add(key);
                            meanings.Add(val);
                        }
                    }
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
