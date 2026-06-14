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
            CheckFunctionalStatus(demands, data),
            CheckSignalMultiplicity(demands),
            CheckSignalNameCase(demands, data),
            CheckL21CrossArch(demands, data),
            CheckCoding(demands, data),
            CheckAnalogBits(demands, data),
            CheckMagicSignals(demands, data),
            CheckUpdateTimeAll(demands, data),
            CheckWhitespace(demands),
            CheckMultisender(demands, data),
            CheckNewTxChannel(demands, data),
            CheckNetworkRoute(demands, data),
            CheckContainerDecision(demands, data),
            CheckL3DigitalStates(demands),
            CheckOtherRequirements(demands, data),
            CheckL3FrameAssignment(demands),   // runs last: inspects the other findings
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

    // Part F: recomputed functional status (from ISR-Applied) vs the Message List 'Functional' flag.
    private CheckSummary CheckFunctionalStatus(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        bool hasFlagColumn = data.Signals.Any(s => s.FunctionalFlag.Length > 0);
        if (!hasFlagColumn)
            return new() { Section = "Basic check", Name = "Functional status (ISR-Applied vs flag)", Kind = "Tool", Ran = false };

        int checkedN = 0;
        foreach (var d in demands)
        {
            var sig = d.MatchedDef; if (sig is null) continue;
            checkedN++;
            bool flag = sig.FunctionalFlag.Trim().Equals("x", StringComparison.OrdinalIgnoreCase);
            if (flag != sig.Functional)
                d.Results.Add(new ValidationResult("Functional status", Severity.Info,
                    $"Message-List flag ({(flag ? "x" : "blank")}) disagrees with recomputed status ({(sig.Functional ? "functional" : "non-functional")}) from ISR-Applied."));
        }
        return Sum("Basic check", "Functional status (ISR-Applied vs flag)", checkedN, 0, 0, "Functional status");
    }

    // Item 5: L2.1 cross-architecture bit/byte cross-check (needs the 2nd architecture loaded).
    private CheckSummary CheckL21CrossArch(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        if (data.SecondArchByName is null)
            return new() { Section = "Main validation", Name = "L2.1 cross-architecture bit/byte", Kind = "Tool", Ran = false };

        var parser = new SignalDataParser();
        int checkedN = 0, warn = 0;
        foreach (var d in demands)
        {
            if (d.Level != IsrLevel.Level2_1) continue;
            var other = d.MatchedDef; if (other is null) continue;   // for L2.1 the matched def is the 2nd-arch row
            checkedN++;

            d.Results.Add(new ValidationResult("L2.1 layout", Severity.Info,
                $"2nd-arch layout: frame {other.FrameName} [{other.FrameType}], PDU {other.PduName}, "
                + $"{other.SignalSizeBits?.ToString() ?? "?"} bits @ byte {(other.BytePosition.Length > 0 ? other.BytePosition : "?")}/bit {(other.BitPosition.Length > 0 ? other.BitPosition : "?")}. Replicate this layout."));

            var p = parser.Parse(d.LogicalData, d.AnalogData);
            int? declared = p.States is int st && st >= 2 ? (int)Math.Ceiling(Math.Log2(st)) : null;
            if (declared is int db && other.SignalSizeBits is int ob && db != ob)
            {
                warn++;
                d.Results.Add(new ValidationResult("L2.1 layout", Severity.Warning,
                    $"Declared size {db} bits ≠ 2nd-architecture size {ob} bits — bit/byte layout differs across architectures."));
            }
        }
        return Sum("Main validation", "L2.1 cross-architecture bit/byte", checkedN, 0, warn, "L2.1 layout");
    }

    // Part A: a signal maps into several frames — surface it (don't silently match one).
    private CheckSummary CheckSignalMultiplicity(IReadOnlyList<IsrDemand> demands)
    {
        int checkedN = 0;
        foreach (var d in demands)
        {
            if (d.MatchedFrames.Count <= 1) continue;
            checkedN++;
            var frames = string.Join(", ", d.MatchedFrames.Select(f =>
                f.FrameName + (string.IsNullOrEmpty(f.FrameType) ? "" : $" [{f.FrameType}]")));
            d.Results.Add(new ValidationResult("Frame multiplicity", Severity.Info,
                $"Signal maps into {d.MatchedFrames.Count} frames: {frames}. Matched → {d.MatchedDef?.FrameName ?? "?"}."));
        }
        return Sum("Main validation", "Signal frame multiplicity", checkedN, 0, 0, "Frame multiplicity");
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
            var sig = d.MatchedDef; if (sig is null || sig.IsStructural) continue;   // resolved frame instance (Part A)
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
            var sig = d.MatchedDef; if (sig is null || sig.IsStructural) continue;   // resolved frame instance (Part A)
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
            var sig = d.MatchedDef; if (sig is null) continue;
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
            if (d.MatchedDef is { } sig) { period = sig.Period; excl = sig.ExclTime; }
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
            var sig = d.MatchedDef; if (sig is null || sig.IsStructural) continue;   // resolved frame instance (Part A)
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

    // Part C: an L2 demand adding a NEW transmitter is only allowed if the new Tx is on the SAME channel
    // as the existing transmitter(s). Channel derived from Network Path (cross-ref ISR-Applied via Msg-List Tx).
    private CheckSummary CheckNewTxChannel(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        int checkedN = 0, err = 0, warn = 0;
        foreach (var d in demands)
        {
            if (d.Level != IsrLevel.Level2) continue;
            var sig = d.MatchedDef; if (sig is null) continue;
            var emitter = (d.Emitter ?? "").Trim();
            if (emitter.Length == 0) continue;
            var existingTxs = sig.Transmitters.ToList();
            if (existingTxs.Count == 0) continue;                       // who transmits today is unknown
            if (existingTxs.Any(t => t.Equals(emitter, StringComparison.OrdinalIgnoreCase))) continue;  // new-Rx, not new-Tx

            checkedN++;
            var newCh = data.ChannelsOf(emitter);
            var existCh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in existingTxs) existCh.UnionWith(data.ChannelsOf(t));

            if (newCh.Count == 0 || existCh.Count == 0)
            {
                warn++;
                d.Results.Add(new ValidationResult("New Tx channel", Severity.Warning,
                    $"New Tx '{emitter}' — channel unknown (no Network-Path entry); same-channel rule could not be verified."));
            }
            else if (!newCh.Overlaps(existCh))
            {
                err++;
                d.Results.Add(new ValidationResult("New Tx channel", Severity.Error,
                    $"New Tx '{emitter}' on channel [{string.Join("/", newCh)}] differs from existing Tx channel [{string.Join("/", existCh)}] — not permitted."));
            }
            // else: same channel → allowed (no finding)
        }
        return Sum("Level", "L2 new-Tx same-channel rule", checkedN, err, warn, "New Tx channel");
    }

    // Part C: L3 (new signal) needs the customer to assign a frame first — gate it (after properties are validated).
    private CheckSummary CheckL3FrameAssignment(IReadOnlyList<IsrDemand> demands)
    {
        int checkedN = 0, err = 0, warn = 0;
        foreach (var d in demands)
        {
            if (d.Level != IsrLevel.Level3) continue;
            checkedN++;
            bool propertyErrors = d.Results.Any(r => r.Rule != "Level" && r.Severity == Severity.Error);
            if (propertyErrors)
            {
                warn++;
                d.Results.Add(new ValidationResult("L3 frame", Severity.Warning,
                    "Blocked — fix the signal's property errors before frame assignment."));
            }
            else if (string.IsNullOrWhiteSpace(d.AssignedFrame))
            {
                err++;
                d.Results.Add(new ValidationResult("L3 frame", Severity.Error,
                    "Properties OK — awaiting customer frame assignment (set 'Assigned frame'). Tool will not auto-assign."));
            }
            else
            {
                d.Results.Add(new ValidationResult("L3 frame", Severity.Info,
                    $"Customer frame assigned: '{d.AssignedFrame}'. Proceed to container decision + create transmissions."));
            }
        }
        return Sum("Level", "L3 frame assignment (customer)", checkedN, err, warn, "L3 frame");
    }

    // FindRxandTx equivalent: does a Network Path route exist for this PDU/frame + Tx + Rx?
    private CheckSummary CheckNetworkRoute(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        if (data.Routes.Count == 0)
            return new() { Section = "Main validation", Name = "Network route (Tx->Rx path)", Kind = "Tool", Ran = false };
        int checkedN = 0, warn = 0;
        foreach (var d in demands)
        {
            var sig = d.MatchedDef;
            var pdu = sig?.PduName ?? "";
            var frame = sig is not null ? sig.FrameName : (string.IsNullOrEmpty(d.FilledFrame) ? d.Frame : d.FilledFrame);
            if (pdu.Length == 0 && string.IsNullOrEmpty(frame)) continue;
            if (string.IsNullOrEmpty(d.Emitter) || string.IsNullOrEmpty(d.Receiver)) continue;
            checkedN++;

            var cont = sig is null ? null : FrameTraceService.ResolveContainer(sig, data);
            var route = FrameTraceService.ResolveRoute(d, sig, cont, data);   // container-aware (Part E)
            if (route is not null) continue;                                   // route SET — found + shown in the trace

            // (b) Existing transmissions (L0/L1) are already routed — never flag a missing hop, even if our
            // matching didn't locate the row. Only a genuinely new Tx→Rx (L2 new-Rx / L2.1 / L3) needs routing.
            if (d.Level is IsrLevel.Level0 or IsrLevel.Level1) continue;

            // Diagnose which hop is missing.
            bool FrameMatch(NetworkRoute r) =>
                (pdu.Length > 0 && r.PduName.Equals(pdu, StringComparison.OrdinalIgnoreCase)) ||
                (frame.Length > 0 && r.FrameName.Equals(frame, StringComparison.OrdinalIgnoreCase));
            bool frameRouted = data.Routes.Any(FrameMatch);
            bool txRouted = data.Routes.Any(r => FrameMatch(r) &&
                (r.Transmitter.Trim().Equals((d.Emitter ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                 || (cont is not null && r.Transmitter.Trim().Equals(cont.TxUnit.Trim(), StringComparison.OrdinalIgnoreCase))));

            string why = !frameRouted
                ? $"frame/PDU '{(pdu.Length > 0 ? pdu : frame)}' has no Network-Path entry"
                : !txRouted
                    ? $"transmitter '{d.Emitter}'{(cont is not null ? $"/'{cont.TxUnit}'" : "")} is not routed for this frame"
                    : $"no route to receiver '{d.Receiver}' (segment pairing not gatewayed today)";
            warn++;
            d.Results.Add(new ValidationResult("Network route", Severity.Warning,
                $"No route — {why}. New gateway routing likely required."));
        }
        return Sum("Main validation", "Network route (Tx->Rx path)", checkedN, 0, warn, "Network route");
    }

    // Part D: container-frame decision (ASIL). Flag undetermined ASIL + container-type mismatches.
    private CheckSummary CheckContainerDecision(IReadOnlyList<IsrDemand> demands, ReferenceData data)
    {
        var decider = new ContainerDecisionService();
        int checkedN = 0, err = 0, warn = 0;
        foreach (var d in demands)
        {
            var sig = d.MatchedDef; if (sig is null) continue;
            var asil = AsilDetector.Detect(d.LossLinkageAsil, d.CorruptDataAsil);

            if (asil == AsilState.Undetermined)
            {
                checkedN++; warn++;
                d.Results.Add(new ValidationResult("Container/ASIL", Severity.Warning,
                    "ASIL undetermined (LossLinkageASIL / CorruptDataASIL incomplete) — cannot decide CRC/CLK/container."));
                continue;
            }
            if (asil == AsilState.None) continue;   // no ASIL → container is busload-optional, nothing to enforce

            checkedN++;
            var cont = FrameTraceService.ResolveContainer(sig, data);
            var route = FrameTraceService.ResolveRoute(d, sig, cont, data);
            bool crosses = route is not null && FrameTraceService.CrossesGateway(route.SynthesisPath);
            var decision = decider.Decide(true, crosses, sig.IsFd && !crosses);
            bool targetSecure = sig.IsSecuredContainer;

            if (decision.Needed == ContainerNeeded.Secure && !targetSecure)
            {
                err++;
                d.Results.Add(new ValidationResult("Container/ASIL", Severity.Error,
                    $"ASIL + crosses gateway ⇒ SECURE container (*SC_FD, MAC=x) required, but matched frame '{sig.FrameName}' is not secured."));
            }
            else if (decision.Needed == ContainerNeeded.Normal && targetSecure)
            {
                warn++;
                d.Results.Add(new ValidationResult("Container/ASIL", Severity.Warning,
                    $"Secure container '{sig.FrameName}' used where a normal *C_FD suffices (ASIL stays on a single FD channel)."));
            }
        }
        return Sum("Main validation", "Container-frame decision (ASIL)", checkedN, err, warn, "Container/ASIL");
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
            if (d.ReqUnavailableValue.Length > 0 && d.MatchedDef is { } sig
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
