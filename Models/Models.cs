namespace ExchangeFileValidator.Models;

/// <summary>One signal row from the Message List ("(FD+HS) all CAN" / "all PDU"). The reference truth.</summary>
public sealed class SignalDef
{
    public string SignalName { get; set; } = "";
    public string FrameName { get; set; } = "";
    public string FrameIdHex { get; set; } = "";
    public string FrameType { get; set; } = "";
    public string FrameContainer { get; set; } = "";   // "Frame Container" col — the container a classic frame is packed into
    public string PduName { get; set; } = "";          // "Contained I-PDU Name"
    public string BytePosition { get; set; } = "";     // Byte Position in ContainedPDU
    public string BitPosition { get; set; } = "";      // Bit Position in ContainedPDU
    public int? FrameSize { get; set; }                // Frame Size / DLC (bytes), if present
    public int? SignalSizeBits { get; set; }
    public string ValueType { get; set; } = "";
    public string Coding { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string UnavailableValue { get; set; } = "";   // "Unavailable Value (Bin/Hex)" [25]
    public string Event { get; set; } = "";               // "Event" [17]
    public string Unit { get; set; } = "";
    public string Resolution { get; set; } = "";
    public string Offset { get; set; } = "";
    public string Min { get; set; } = "";
    public string Max { get; set; } = "";
    public string TransmissionType { get; set; } = "";
    public string Period { get; set; } = "";
    public string ExclTime { get; set; } = "";

    public string FunctionalFlag { get; set; } = "";   // Message List 'Functional' column (x/blank), if present
    public bool Functional { get; set; }               // RECOMPUTED from ISR-Applied (any active ISR) — Part F
    public string FunctionalMark => Functional ? "x" : "";

    /// <summary>Per-ECU T/R map from the Message List node columns (only non-empty cells).</summary>
    public Dictionary<string, string> EcuTxRx { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ECUs marked as transmitters of this signal.</summary>
    public IEnumerable<string> Transmitters => EcuTxRx.Where(kv => kv.Value.Contains('T', StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key);

    public bool IsCrc => SignalName.StartsWith("CRC_", StringComparison.OrdinalIgnoreCase) || SignalName == "VehicleSpeedCRC";
    public bool IsClock => SignalName.StartsWith("Clock_", StringComparison.OrdinalIgnoreCase) || SignalName == "VehicleSpeedClock";

    /// <summary>Padding rows ("**** fixed to zero ****" / "…Container****") — excluded from signal matching/validation.</summary>
    public bool IsFiller => SignalName.Contains("fixed to zero", StringComparison.OrdinalIgnoreCase);
    public bool IsHeader => SignalName.StartsWith("Header", StringComparison.OrdinalIgnoreCase);
    /// <summary>Frame-structure signals (CRC / Clock / Header) — real, but not application signals to compare ISR props against.</summary>
    public bool IsStructural => IsCrc || IsClock || IsHeader;

    public bool IsFd => FrameType.Contains("FD", StringComparison.OrdinalIgnoreCase);
    public bool IsContainerFrame => FrameType.Contains("container", StringComparison.OrdinalIgnoreCase);
    /// <summary>Secured container variant (*SC_FD, MAC=x).</summary>
    public bool IsSecuredContainer => FrameName.Contains("SC_FD", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One applied/confirmed ISR row from "Nissan_FACE HS applied proposal".</summary>
public sealed class AppliedIsr
{
    public string IsrNumber { get; set; } = "";
    public string Feature { get; set; } = "";
    public string Transmitter { get; set; } = "";
    public string Receiver { get; set; } = "";
    public string Frame { get; set; } = "";
    public string Parameter { get; set; } = "";
    public string AsilLevel { get; set; } = "";
    public string Clk { get; set; } = "";
    public string Crc { get; set; } = "";
    /// <summary>Value of the latest release column (x = applied, A = abandon, R = re-something).</summary>
    public string LatestStatus { get; set; } = "";

    /// <summary>Current status = value in the latest non-empty tranche column (x = Use, A = Abandon, R = Refused).</summary>
    public bool IsActive => LatestStatus.Trim().Equals("x", StringComparison.OrdinalIgnoreCase);

    /// <summary>Level-match key used by the macros: Parameter + Tx + Rx + "x".</summary>
    public string SignalTxRxKey => $"{Parameter}|{Transmitter}|{Receiver}";
    public string SignalTxKey => $"{Parameter}|{Transmitter}";
}

/// <summary>ECU display-name &lt;-&gt; code map (Dico sheet).</summary>
public sealed class EcuDicoEntry
{
    public string Name { get; set; } = "";      // "ECU Msg Set"
    public string Code { get; set; } = "";
    public string IsrStatusName { get; set; } = "";
    public bool Different { get; set; }
}

/// <summary>One CAN-FD container frame from "Construction of Container frame" (the assembly layer).</summary>
public sealed class ContainerFrame
{
    public string FrameName { get; set; } = "";
    public string FrameId { get; set; } = "";
    public string FrameType { get; set; } = "";
    public string TxUnit { get; set; } = "";              // gateway/master that emits the container (PIU_MASTER on FACE)
    public string Mac { get; set; } = "";                 // "x" ⇒ secured variant
    public string TransmissionType { get; set; } = "";
    public string Period { get; set; } = "";
    public string ExclTime { get; set; } = "";
    public string OriginalId { get; set; } = "";
    public string ContainedPdu { get; set; } = "";        // Contained I-PDU name (join key to the signal layer)
    public string OriginalLength { get; set; } = "";
    public string OriginalTransmissionType { get; set; } = "";
    public string OriginalPeriod { get; set; } = "";
    public string OriginalExclTime { get; set; } = "";
    public string OriginalTxUnit { get; set; } = "";      // source ECU

    public bool IsSecured => Mac.Trim().Equals("x", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One layer of the signal→PDU→container→route trace (Part B).</summary>
public sealed record TraceStep(string Layer, string Title, string Detail);

/// <summary>The resolved content→packing→routing chain for a demand.</summary>
public sealed class FrameTrace
{
    public List<TraceStep> Steps { get; } = new();
    public bool HasContainer { get; set; }
    public bool CrossesGateway { get; set; }   // route passes through the CGW / PIU_MASTER
}

/// <summary>Routing row from "Network Path".</summary>
public sealed class NetworkRoute
{
    public string PduName { get; set; } = "";
    public string FrameName { get; set; } = "";
    public string Transmitter { get; set; } = "";
    public string Receiver { get; set; } = "";
    public string SynthesisPath { get; set; } = "";   // e.g. "ACU => V1-CAN => PIU"

    /// <summary>Per-segment 'x' columns (CH1-CAN, CH1-FD, ITS1-6-FD, PT-FD, V1-CAN, …) marked on this route.</summary>
    public HashSet<string> Segments { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum IsrLevel { Unassigned, Level0, Level1, Level2, Level2_1, Level3 }

/// <summary>One incoming ISR demand row (the work item). Filled/validated by the tool.</summary>
public sealed class IsrDemand
{
    public string IsrNumber { get; set; } = "";
    public string FeatureNumber { get; set; } = "";
    public string EmitterCode { get; set; } = "";
    public string Emitter { get; set; } = "";
    public string ReceiverCode { get; set; } = "";
    public string Receiver { get; set; } = "";
    public string Frame { get; set; } = "";
    public string ParameterProposal { get; set; } = "";   // the requested signal
    public string MediaType { get; set; } = "";
    public string NetworkType { get; set; } = "";
    public string SynthesisStatus { get; set; } = "";
    public string UpdateTime { get; set; } = "";
    public string LogicalData { get; set; } = "";
    public string AnalogData { get; set; } = "";
    public string KindOfIsr { get; set; } = "";
    public string OtherRequirements { get; set; } = "";
    public string LossLinkageAsil { get; set; } = "";     // ASIL columns (Part D) — either carrying a value ⇒ ASIL requested
    public string CorruptDataAsil { get; set; } = "";

    // parsed from OtherRequirements
    public string ReqTx { get; set; } = "";
    public string ReqRx { get; set; } = "";
    public string ReqNetworkPath { get; set; } = "";
    public string ReqUnavailableValue { get; set; } = "";
    public string ReqChangeMgmt { get; set; } = "";

    public IsrLevel Level { get; set; } = IsrLevel.Unassigned;
    public string ImportStatus { get; set; } = "";        // "Ready to import" gates export
    public string Note { get; set; } = "";                // per-ISR user note

    // --- Part A: resolved frame instance (a signal maps into many frames; pick the right one once) ---
    public SignalDef? MatchedDef { get; set; }            // the chosen frame instance for this demand
    public List<SignalDef> MatchedFrames { get; set; } = new();  // all frame mappings of the proposed signal
    public string MatchSource { get; set; } = "";         // "Message List" | "2nd architecture" | ""

    // --- Part C: L3 customer frame assignment (gates signal/transmission creation) ---
    public string AssignedFrame { get; set; } = "";       // customer decision: new frame name or an existing frame

    // --- Part B: signal → PDU → container → route trace ---
    public FrameTrace? Trace { get; set; }
    public List<ValidationResult> Results { get; } = new();

    public string LevelLabel => Level switch
    {
        IsrLevel.Level0 => "L0",
        IsrLevel.Level1 => "L1",
        IsrLevel.Level2 => "L2",
        IsrLevel.Level2_1 => "L2.1",
        IsrLevel.Level3 => "L3",
        _ => "—"
    };

    public string LevelNote => Results.FirstOrDefault(r => r.Rule == "Level")?.Message ?? "";
    public string Route => string.IsNullOrEmpty(Emitter) && string.IsNullOrEmpty(Receiver) ? "" : $"{Emitter} → {Receiver}";

    // --- Phase 3: filled from the Message List ---
    public string FilledFrame { get; set; } = "";
    public string FilledFrameId { get; set; } = "";
    public string FilledPdu { get; set; } = "";
    public int? FilledBits { get; set; }
    public string FilledValueType { get; set; } = "";
    public string FilledUnit { get; set; } = "";
    public string FilledMin { get; set; } = "";
    public string FilledMax { get; set; } = "";
    public bool HasCrcOnFrame { get; set; }
    public bool HasClkOnFrame { get; set; }
    public string CrcNote { get; set; } = "";       // Tx/Rx-aware reuse / new-needed note
    public string CrcStatus { get; set; } = "";     // "reuse" | "new" | "present" | "" — drives the CRC badge
    public string ClkStatus { get; set; } = "";     // "reuse" | "new" | "present" | "" — drives the Clock badge

    /// <summary>Side-by-side ISR-vs-AT comparison for the expandable row detail (built by IsrDetailBuilder).</summary>
    public IsrDetail? Detail { get; set; }
    public string FillSource { get; set; } = "";    // "Message List" / "2nd architecture" / ""
    public string FillStatus { get; set; } = "";    // human note
    public string CrcClk => (HasCrcOnFrame ? "CRC " : "") + (HasClkOnFrame ? "CLK" : "");

    /// <summary>Highest-severity validation finding (excluding the Level note), or null if clean.</summary>
    public Severity? WorstSeverity
    {
        get
        {
            Severity? w = null;
            foreach (var r in Results)
            {
                if (r.Rule == "Level") continue;
                if (w is null || r.Severity > w) w = r.Severity;
            }
            return w;
        }
    }

    public string Issues => string.Join("  •  ",
        Results.Where(r => r.Rule != "Level").Select(r => $"[{r.Rule}] {r.Message}"));

    /// <summary>Validation findings for this ISR (excluding the informational Level note), for the inline detail.</summary>
    public IEnumerable<ValidationResult> Findings => Results.Where(r => r.Rule != "Level");
}

public enum Severity { Info, Warning, Error }

public sealed record ValidationResult(string Rule, Severity Severity, string Message);

/// <summary>A flattened finding for the validation report grid.</summary>
public sealed record ValidationRow(Severity Severity, string Rule, string Isr, string Signal, string Message);

/// <summary>One difference between the Exchange File and an external ISR-Status export.</summary>
public sealed record DiffRow(string Isr, string Field, string ExchangeValue, string StatusValue, string Note);

/// <summary>ISR-declared property vs Message-Set definition (point 16).</summary>
public sealed record CompareRow(string Isr, string Signal, string Property, string IsrValue, string MsgSetValue, bool Match)
{
    public string Status => Match ? "Match" : "Mismatch";
}

/// <summary>Expandable per-ISR detail: the side-by-side ISR(online) vs AT(Message Set) comparison + CRC/CLK badges.</summary>
public sealed class IsrDetail
{
    /// <summary>True when the proposed signal was matched in the Message Set (or 2nd architecture).</summary>
    public bool HasAt { get; init; }

    /// <summary>Property-by-property comparison rows (ISR value vs Message-Set value, with a Match flag).</summary>
    public List<CompareRow> Props { get; } = new();

    /// <summary>Findings grouped into category cards for the detail panel (Fix 6).</summary>
    public List<FindingGroup> Groups { get; } = new();

    // CRC / Clock badges (text + state for colouring): state ∈ { reuse, new, present, "" }
    public string CrcBadge { get; init; } = "";
    public string CrcState { get; init; } = "";
    public string ClkBadge { get; init; } = "";
    public string ClkState { get; init; } = "";
}

/// <summary>A category card of findings for the per-ISR detail panel (Fix 6).</summary>
public sealed class FindingGroup
{
    public string Category { get; init; } = "";
    public List<ValidationResult> Items { get; } = new();
    public Severity HeaderSeverity { get; set; }
    public int Count => Items.Count;
}

/// <summary>One row of the validation checklist dashboard.</summary>
public sealed class CheckSummary
{
    public string Section { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>The ValidationResult.Rule this check emits, so the checklist can jump to its findings.</summary>
    public string Rule { get; init; } = "";
    public string Kind { get; init; } = "Tool";     // "Tool" | "Manual" | "External"
    public bool Ran { get; init; }
    public int Checked { get; init; }
    public int Passed { get; init; }
    public int Warned { get; init; }
    public int Errored { get; init; }

    public string Status =>
        !Ran ? Kind switch { "Manual" => "Manual", "External" => "External tool", _ => "Not run" }
        : Errored > 0 ? "Error"
        : Warned > 0 ? "Warning"
        : "Pass";

    public string Result => !Ran ? "—" : $"{Passed}/{Checked} ok"
        + (Warned > 0 ? $", {Warned} warn" : "")
        + (Errored > 0 ? $", {Errored} err" : "");
}
