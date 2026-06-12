using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Services;

/// <summary>
/// Validation suite mirroring the Validation Points checklist + ExchangeFileValidation_C1AHS macro.
/// Each tool check appends findings to demands AND returns a CheckSummary for the checklist dashboard.
/// Manual / external checklist steps are listed too (so the dashboard is complete and honest).
/// </summary>
public sealed class ValidationService
{
    private sealed record Profile(string Pattern, int? Bits, string? Coding, string? Transmission, string? Period, string? Meaning);

    private static readonly Profile[] MagicProfiles =
    {
        new("DTOOL",              64, "",       "Diagnostic",          "-",   "Frame content is defined in Diagnostic specification"),
        new("FTOOL",              64, "",       "Event",               "-",   null),
        new("WakeUpType",          3, "0b111",  "Periodic and Event",  "100", null),
        new("WakeUp_Signal",      16, "0x83C0", "Event",               "-",   null),
        new("WakeUpSleepCommand",  2, "0b11",   "Periodic and Event",  "100", null),
        new("SIGMA_DTC",          24, "",       "Event",               "-",   null),
        new("ReadMeReq",          64, "",       "Event",               "-",   "refer to diag specification"),
    };

    public List<CheckSummary> Validate(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        foreach (var d in demands) d.Results.RemoveAll(r => r.Rule != "Level");

        var s = new List<CheckSummary>
        {
            CheckEmptyFields(demands),
            CheckLogicalAnalogExclusive(demands),
            CheckDico(demands, data),
            LevelSummary(demands),
            CheckDuplicates(demands),
            CheckSignalInAt(demands, data),
            CheckSignalNameCase(demands, data),
            CheckCoding(demands, data),
            CheckAnalogBits(demands, data),
            CheckMagicSignals(demands, data),
            CheckUpdateTimeAll(demands, data),
            CheckWhitespace(demands),
            CheckMultisender(demands, data),
            CheckNetworkRoute(demands, data),
            CheckL3DigitalStates(demands),
            CheckOtherRequirements(demands, data),
            AutoDone("Basic check", "Replace ECU names (FACE): PIU_Mst->PIU_MASTER etc.", demands.Count),
            // manual / external checklist steps that remain
            Manual("Preparation", "Prepare Exchange File + copy msg-set sheets + AEEA name"),
            Manual("Main validation", "Cross-check frame props / bit-byte position (L2.1)"),
            External("Main validation", "ISR Online vs Exchange File (AT validation tool)"),
            Manual("Main validation", "Other Requirements check (two reviewers)"),
        };
        return s;
    }

    private static CheckSummary Manual(string sec, string name) => new() { Section = sec, Name = name, Kind = "Manual", Ran = false };
    private static CheckSummary External(string sec, string name) => new() { Section = sec, Name = name, Kind = "External", Ran = false };

    private static readonly (string disp, Func<IsrDemand, string> get)[] Mandatory =
    {
        ("ISR_Number", d => d.IsrNumber), ("Feature_Number", d => d.FeatureNumber),
        ("EmitterCode", d => d.EmitterCode), ("Emitter", d => d.Emitter),
        ("ReceiverCode", d => d.ReceiverCode), ("Receiver", d => d.Receiver),
        ("Media Type", d => d.MediaType), ("NetworkType", d => d.NetworkType),
        ("ParameterProposal", d => d.ParameterProposal), ("UpdateTime", d => d.UpdateTime),
    };

    private CheckSummary CheckEmptyFields(IReadOnlyList<IsrDemand> demands)
    {
        int err = 0;
        foreach (var d in demands)
        {
            var missing = Mandatory.Where(m => string.IsNullOrWhiteSpace(m.get(d))).Select(m => m.disp).ToList();
            if (string.IsNullOrWhiteSpace(d.LogicalData) && string.IsNullOrWhiteSpace(d.AnalogData))
                missing.Add("Logical/Analog data");
            if (missing.Count > 0)
            {
                err++;
                d.Results.Add(new ValidationResult("Empty field", Severity.Error, "Empty: " + string.Join(", ", missing)));
            }
        }
        return Sum("Basic check", "Empty mandatory fields", demands.Count, err, 0, "Empty field");
    }

    private CheckSummary CheckLogicalAnalogExclusive(IReadOnlyList<IsrDemand> demands)
    {
        int err = 0;
        foreach (var d in demands)
            if (!string.IsNullOrWhiteSpace(d.LogicalData) && !string.IsNullOrWhiteSpace(d.AnalogData))
            {
                err++;
                d.Results.Add(new ValidationResult("Logical/Analog", Severity.Error, "Logical and Analog data both present (mutually exclusive)."));
            }
        return Sum("Basic check", "Logical / Analog exclusivity", demands.Count, err, 0, "Logical/Analog");
    }

    private static CheckSummary LevelSummary(IReadOnlyList<IsrDemand> demands)
    {
        int unassigned = demands.Count(d => d.Level == IsrLevel.Unassigned);
        return Sum("Level", "Level assignment (L0-L3 / L2.1)", demands.Count, 0, unassigned);
    }

    private CheckSummary CheckDuplicates(IReadOnlyList<IsrDemand> demands)
    {
        int err = 0;
        foreach (var g in demands.Where(d => !string.IsNullOrWhiteSpace(d.IsrNumber))
                                 .GroupBy(d => d.IsrNumber.Trim(), StringComparer.OrdinalIgnoreCase)
                                 .Where(g => g.Count() > 1))
            foreach (var d in g)
            {
                err++;
                d.Results.Add(new ValidationResult("Duplicate ISR", Severity.Error, $"ISR n appears on {g.Count()} rows."));
            }
        return Sum("Main validation", "Duplicate ISR", demands.Count, err, 0, "Duplicate ISR");
    }

    private CheckSummary CheckSignalInAt(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        int checkedN = 0, err = 0, warn = 0;
        foreach (var d in demands)
        {
            if (string.IsNullOrWhiteSpace(d.ParameterProposal)) continue;
            bool inAt = data.SignalByName.ContainsKey(d.ParameterProposal);
            if (d.Level is IsrLevel.Level0 or IsrLevel.Level1 or IsrLevel.Level2)
            {
                checkedN++;
                if (!inAt) { err++; d.Results.Add(new ValidationResult("Signal in AT", Severity.Error, $"{d.LevelLabel} signal not found in Message List.")); }
            }
            else if (d.Level == IsrLevel.Level3)
            {
                checkedN++;
                if (inAt) { warn++; d.Results.Add(new ValidationResult("Signal in AT", Severity.Warning, "L3 (new) but signal already exists in Message List.")); }
            }
        }
        return Sum("Main validation", "Signal present in AT", checkedN, err, warn, "Signal in AT");
    }

    private CheckSummary CheckSignalNameCase(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        int checkedN = 0, err = 0;
        foreach (var d in demands)
        {
            if (d.Level is not (IsrLevel.Level1 or IsrLevel.Level2 or IsrLevel.Level2_1)) continue;
            if (string.IsNullOrWhiteSpace(d.ParameterProposal)) continue;
            if (!data.SignalByName.TryGetValue(d.ParameterProposal, out var sig)) continue;
            checkedN++;
            if (!string.Equals(d.ParameterProposal, sig.SignalName, StringComparison.Ordinal))
            {
                err++;
                d.Results.Add(new ValidationResult("Signal name case", Severity.Error,
                    $"Case mismatch: '{d.ParameterProposal}' vs Message List '{sig.SignalName}'."));
            }
        }
        return Sum("Main validation", "Signal name case-exact (L1/2/2.1)", checkedN, err, 0, "Signal name case");
    }

    private CheckSummary CheckCoding(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        int checkedN = 0, warn = 0;
        foreach (var d in demands)
        {
            if (!data.SignalByName.TryGetValue(d.ParameterProposal ?? "", out var sig)) continue;
            if (sig.SignalSizeBits is not int bits || bits < 1 || bits > 6) continue;
            if (string.IsNullOrWhiteSpace(sig.Meaning)) continue;
            checkedN++;
            int expected = 1 << bits;
            int actual = sig.Meaning.Split('\n').Count(x => x.Trim().Length > 0);
            if (actual != expected)
            {
                warn++;
                d.Results.Add(new ValidationResult("Coding/bit-size", Severity.Warning,
                    $"{bits}-bit signal has {actual} meaning lines (expected {expected})."));
            }
        }
        return Sum("Main validation", "Coding / meaning vs bit-size", checkedN, 0, warn, "Coding/bit-size");
    }

    private CheckSummary CheckAnalogBits(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        int checkedN = 0, warn = 0;
        foreach (var d in demands)
        {
            if (!data.SignalByName.TryGetValue(d.ParameterProposal ?? "", out var sig)) continue;
            if (sig.SignalSizeBits is not int bits) continue;
            if (!double.TryParse(sig.Min, out var min) || !double.TryParse(sig.Max, out var max)
                || !double.TryParse(sig.Resolution, out var res) || res == 0) continue;
            double states = (max - min) / res + 1;
            if (states <= 1) continue;
            int required = (int)Math.Ceiling(Math.Log2(states));
            checkedN++;
            if (required != bits)
            {
                warn++;
                d.Results.Add(new ValidationResult("Analog bit-size", Severity.Warning,
                    $"Analog needs {required} bits for range/resolution (defined {bits})."));
            }
        }
        return Sum("Main validation", "Analog signal bit-size (L3)", checkedN, 0, warn, "Analog bit-size");
    }

    private CheckSummary CheckMagicSignals(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        int checkedN = 0, err = 0;
        foreach (var d in demands)
        {
            var name = d.ParameterProposal ?? "";
            var p = MagicProfiles.FirstOrDefault(x => name.Contains(x.Pattern, StringComparison.OrdinalIgnoreCase));
            if (p is null) continue;
            if (!data.SignalByName.TryGetValue(name, out var sig)) continue;
            checkedN++;
            var bad = new List<string>();
            if (p.Bits is int b && sig.SignalSizeBits != b) bad.Add($"size {sig.SignalSizeBits}!={b}");
            if (p.Coding is not null && !Eq(sig.Coding, p.Coding)) bad.Add($"coding '{sig.Coding}'!='{p.Coding}'");
            if (p.Transmission is not null && !Eq(sig.TransmissionType, p.Transmission)) bad.Add($"tx '{sig.TransmissionType}'!='{p.Transmission}'");
            if (p.Period is not null && !Eq(sig.Period, p.Period)) bad.Add($"period '{sig.Period}'!='{p.Period}'");
            if (p.Meaning is not null && !Eq(sig.Meaning, p.Meaning)) bad.Add("meaning differs");
            if (bad.Count > 0)
            {
                err++;
                d.Results.Add(new ValidationResult("Magic signal", Severity.Error,
                    $"{p.Pattern} profile mismatch: {string.Join("; ", bad)}."));
            }
        }
        return Sum("Main validation", "Special-signal profiles (DTOOL/WakeUp/SIGMA...)", checkedN, err, 0, "Magic signal");
    }

    private CheckSummary CheckUpdateTimeAll(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        int checkedN = 0, warn = 0;
        foreach (var d in demands)
        {
            if (string.IsNullOrWhiteSpace(d.UpdateTime)) continue;
            string period = "", excl = "";
            if (data.SignalByName.TryGetValue(d.ParameterProposal ?? "", out var sig)) { period = sig.Period; excl = sig.ExclTime; }
            checkedN++;
            if (!UpdateTimeRule.Check(d.UpdateTime, period, excl))
            {
                warn++;
                d.Results.Add(new ValidationResult("Update time", Severity.Warning,
                    $"UpdateTime '{d.UpdateTime}' inconsistent with period '{period}' / excl '{excl}'."));
            }
        }
        return Sum("Main validation", "Update-time rule", checkedN, 0, warn, "Update time");
    }

    private CheckSummary CheckDico(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        if (data.Dico.Count == 0) return new() { Section = "Basic check", Name = "ECU code (Dico)", Kind = "Tool", Ran = false };
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in data.Dico) byName[e.Name] = e.Code;
        int checkedN = 0, err = 0, warn = 0;
        foreach (var d in demands) { Verify(d, d.Emitter, d.EmitterCode, "Emitter"); Verify(d, d.Receiver, d.ReceiverCode, "Receiver"); }
        return Sum("Basic check", "ECU code (Dico)", checkedN, err, warn, "ECU code (Dico)");

        void Verify(IsrDemand d, string nm, string code, string role)
        {
            if (string.IsNullOrWhiteSpace(nm) || string.IsNullOrWhiteSpace(code)) return;
            checkedN++;
            if (!byName.TryGetValue(nm.Trim(), out var exp)) { warn++; d.Results.Add(new ValidationResult("ECU code (Dico)", Severity.Warning, $"{role} '{nm}' not in Dico.")); }
            else if (!Eq(exp, code)) { err++; d.Results.Add(new ValidationResult("ECU code (Dico)", Severity.Error, $"{role} '{nm}' code {code} != Dico {exp}.")); }
        }
    }

    private static CheckSummary AutoDone(string sec, string name, int n) => new()
    { Section = sec, Name = name + "  (applied automatically at load)", Kind = "Tool", Ran = true, Checked = n, Passed = n };

    private CheckSummary CheckWhitespace(IReadOnlyList<IsrDemand> demands)
    {
        int err = 0;
        foreach (var d in demands)
        {
            var bad = new List<string>();
            void T(string v, string nm) { if (v.Length > 0 && (v != v.Trim())) bad.Add(nm); }
            T(d.IsrNumber, "ISR_Number"); T(d.ParameterProposal, "ParameterProposal");
            T(d.Emitter, "Emitter"); T(d.Receiver, "Receiver"); T(d.Frame, "Frame");
            if (bad.Count > 0)
            {
                err++;
                d.Results.Add(new ValidationResult("Whitespace", Severity.Warning,
                    "Leading/trailing space in: " + string.Join(", ", bad)));
            }
        }
        return Sum("Basic check", "Leading/trailing whitespace", demands.Count, 0, err, "Whitespace");
    }

    // Checklist "Multisender Check for Level 2": adding a new Tx to an existing signal.
    private CheckSummary CheckMultisender(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        int checkedN = 0, warn = 0;
        foreach (var d in demands)
        {
            if (d.Level != IsrLevel.Level2) continue;
            if (!data.SignalByName.TryGetValue(d.ParameterProposal ?? "", out var sig)) continue;
            var txs = sig.Transmitters.ToList();
            if (txs.Count == 0) continue;   // node columns not detected
            checkedN++;
            bool already = txs.Any(t => t.Equals((d.Emitter ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (!already)
            {
                warn++;
                d.Results.Add(new ValidationResult("Multisender (L2)", Severity.Warning,
                    $"New transmitter '{d.Emitter}' - existing: {string.Join(", ", txs)}. Multisender review needed."));
            }
        }
        return Sum("Level", "Multisender check (L2)", checkedN, 0, warn, "Multisender (L2)");
    }

    // FindRxandTx equivalent: does a Network Path route exist for this PDU/frame + Tx + Rx?
    private CheckSummary CheckNetworkRoute(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        if (data.Routes.Count == 0)
            return new() { Section = "Main validation", Name = "Network route (Tx->Rx path)", Kind = "Tool", Ran = false };
        int checkedN = 0, warn = 0;
        foreach (var d in demands)
        {
            var pdu = string.IsNullOrEmpty(d.FilledPdu) ? null : d.FilledPdu;
            var frame = string.IsNullOrEmpty(d.FilledFrame) ? d.Frame : d.FilledFrame;
            if (pdu is null && string.IsNullOrEmpty(frame)) continue;
            if (string.IsNullOrEmpty(d.Emitter) || string.IsNullOrEmpty(d.Receiver)) continue;
            checkedN++;
            bool found = data.Routes.Any(r =>
                ((pdu is not null && r.PduName.Equals(pdu, StringComparison.OrdinalIgnoreCase))
                 || (frame.Length > 0 && r.FrameName.Equals(frame, StringComparison.OrdinalIgnoreCase)))
                && r.Transmitter.Equals(d.Emitter.Trim(), StringComparison.OrdinalIgnoreCase)
                && r.Receiver.Equals(d.Receiver.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!found)
            {
                warn++;
                d.Results.Add(new ValidationResult("Network route", Severity.Warning,
                    $"No Network Path entry for {pdu ?? frame}: {d.Emitter} -> {d.Receiver}. New routing may be needed."));
            }
        }
        return Sum("Main validation", "Network route (Tx->Rx path)", checkedN, 0, warn, "Network route");
    }

    // L3 digital: count Etat_ states in LogicalData -> required bits (info for definition work).
    private CheckSummary CheckL3DigitalStates(IReadOnlyList<IsrDemand> demands)
    {
        int checkedN = 0;
        foreach (var d in demands)
        {
            if (d.Level != IsrLevel.Level3 || string.IsNullOrWhiteSpace(d.LogicalData)) continue;
            int states = System.Text.RegularExpressions.Regex.Matches(d.LogicalData, @"Etat_?[0-9]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
            if (states < 2) continue;
            checkedN++;
            int bits = (int)Math.Ceiling(Math.Log2(states));
            d.Results.Add(new ValidationResult("L3 digital sizing", Severity.Info,
                $"{states} logical states -> needs {bits}-bit signal (+1 state margin if unavailable value required)."));
        }
        return Sum("Main validation", "L3 digital signal sizing", checkedN, 0, 0, "L3 digital sizing");
    }

    // Point 17 — OtherRequirements (Tx/Rx/UV) must agree with the ISR + signal properties.
    private CheckSummary CheckOtherRequirements(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        int checkedN = 0, err = 0, warn = 0;
        foreach (var d in demands)
        {
            if (string.IsNullOrWhiteSpace(d.OtherRequirements)) continue;
            checkedN++;
            // Tx / Rx must match the demand's emitter / receiver (ECU names already normalized)
            if (d.ReqTx.Length > 0 && !Eq(NormEcuLocal(d.ReqTx), d.Emitter))
            { err++; d.Results.Add(new ValidationResult("Other Req", Severity.Error, $"OtherReq Tx '{d.ReqTx}' != Emitter '{d.Emitter}'.")); }
            if (d.ReqRx.Length > 0 && !Eq(NormEcuLocal(d.ReqRx), d.Receiver))
            { err++; d.Results.Add(new ValidationResult("Other Req", Severity.Error, $"OtherReq Rx '{d.ReqRx}' != Receiver '{d.Receiver}'.")); }

            // UnavailableValue format: hex (0x) for >4-bit signals, binary (0b) otherwise
            if (d.ReqUnavailableValue.Length > 0 && data.SignalByName.TryGetValue(d.ParameterProposal ?? "", out var sig)
                && sig.SignalSizeBits is int bits)
            {
                var uv = d.ReqUnavailableValue.Trim();
                bool wantHex = bits > 4;
                bool isHex = uv.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                bool isBin = uv.StartsWith("0b", StringComparison.OrdinalIgnoreCase);
                if (wantHex && !isHex && (isBin || uv.Length > 0))
                { warn++; d.Results.Add(new ValidationResult("Other Req", Severity.Warning, $"UnavailableValue '{uv}' should be hex (0x) for a {bits}-bit signal.")); }
                else if (!wantHex && !isBin && isHex)
                { warn++; d.Results.Add(new ValidationResult("Other Req", Severity.Warning, $"UnavailableValue '{uv}' should be binary (0b) for a {bits}-bit signal.")); }
            }
        }
        return Sum("Main validation", "Other Requirements (Tx/Rx/UV)", checkedN, err, warn, "Other Req");
    }

    private static readonly Dictionary<string, string> EcuMap = new(StringComparer.OrdinalIgnoreCase)
    { ["PIU_Mst"] = "PIU_MASTER", ["PIU_Hood"] = "PIU_HOOD", ["PIU_Sub"] = "PIU_SUB" };
    private static string NormEcuLocal(string n) => EcuMap.TryGetValue((n ?? "").Trim(), out var v) ? v : (n ?? "").Trim();

    private static bool Eq(string a, string b) => (a ?? "").Trim().Equals((b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static CheckSummary Sum(string sec, string name, int checkedN, int err, int warn, string rule = "") => new()
    {
        Section = sec, Name = name, Rule = rule, Kind = "Tool", Ran = true,
        Checked = checkedN, Passed = Math.Max(0, checkedN - err - warn), Warned = warn, Errored = err
    };

    public static List<ValidationRow> ToRows(IEnumerable<IsrDemand> demands) =>
        demands.SelectMany(d => d.Results.Where(r => r.Rule != "Level")
                .Select(r => new ValidationRow(r.Severity, r.Rule, d.IsrNumber, d.ParameterProposal, r.Message)))
            .OrderByDescending(r => r.Severity).ToList();
}
