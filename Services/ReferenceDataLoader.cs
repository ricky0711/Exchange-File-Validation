using ClosedXML.Excel;
using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Services;

/// <summary>Maps a header row to column numbers by normalized name, so we never hardcode indices.</summary>
public sealed class ColumnMap
{
    private readonly Dictionary<string, int> _byName = new(StringComparer.OrdinalIgnoreCase);

    public static string Norm(string? s) =>
        (s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("  ", " ").Trim();

    public ColumnMap(IXLRow headerRow)
    {
        foreach (var cell in headerRow.CellsUsed())
        {
            var key = Norm(cell.GetString());
            if (key.Length > 0 && !_byName.ContainsKey(key))
                _byName[key] = cell.Address.ColumnNumber;
        }
    }

    /// <summary>First matching header from the candidates, or 0 if none found.</summary>
    public int Col(params string[] candidates)
    {
        foreach (var c in candidates)
            if (_byName.TryGetValue(Norm(c), out var n)) return n;
        return 0;
    }

    public string Get(IXLRow row, int col) => col == 0 ? "" : row.Cell(col).GetString().Trim();
}

public sealed class ReferenceData
{
    public List<SignalDef> Signals { get; } = new();
    /// <summary>First (primary) frame mapping per signal name — kept for convenience.</summary>
    public Dictionary<string, SignalDef> SignalByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>ALL frame mappings per signal name (a signal maps into many frames). Fillers excluded.</summary>
    public Dictionary<string, List<SignalDef>> SignalMappingsByName { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the multi-mapping index from <see cref="Signals"/>, skipping filler/padding rows.</summary>
    public void IndexSignals()
    {
        SignalByName.Clear();
        SignalMappingsByName.Clear();
        foreach (var sig in Signals)
        {
            if (sig.IsFiller || string.IsNullOrEmpty(sig.SignalName)) continue;   // padding rows aren't matchable
            if (!SignalMappingsByName.TryGetValue(sig.SignalName, out var list))
            {
                list = new List<SignalDef>();
                SignalMappingsByName[sig.SignalName] = list;
                SignalByName[sig.SignalName] = sig;     // first mapping = primary
            }
            list.Add(sig);
        }
    }
    public List<AppliedIsr> AppliedIsrs { get; } = new();
    public List<EcuDicoEntry> Dico { get; } = new();
    public List<NetworkRoute> Routes { get; } = new();
    public List<IsrDemand> Demands { get; } = new();

    /// <summary>Container frames from "Construction of Container frame" (assembly layer).</summary>
    public List<ContainerFrame> Containers { get; } = new();
    /// <summary>Container frames indexed by Contained I-PDU name (the signal-layer join key).</summary>
    public Dictionary<string, List<ContainerFrame>> ContainersByPdu { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Container frames indexed by frame name.</summary>
    public Dictionary<string, ContainerFrame> ContainersByFrame { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void IndexContainers()
    {
        ContainersByPdu.Clear();
        ContainersByFrame.Clear();
        foreach (var c in Containers)
        {
            if (c.ContainedPdu.Length > 0)
            {
                if (!ContainersByPdu.TryGetValue(c.ContainedPdu, out var l)) ContainersByPdu[c.ContainedPdu] = l = new();
                l.Add(c);
            }
            if (c.FrameName.Length > 0) ContainersByFrame.TryAdd(c.FrameName, c);
        }
    }

    /// <summary>Optional second-architecture signal defs (for Level 2.1 + fill). Set when the user loads one.</summary>
    public Dictionary<string, SignalDef>? SecondArchByName { get; set; }

    /// <summary>ECU → its home channel segment(s), derived from Network Path (Part C same-channel rule).</summary>
    public Dictionary<string, HashSet<string>> EcuChannels { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Home channel(s) of a transmitter = the segments common to ALL of its transmit routes.</summary>
    public void IndexChannels()
    {
        EcuChannels.Clear();
        foreach (var g in Routes.Where(r => r.Transmitter.Trim().Length > 0)
                                .GroupBy(r => r.Transmitter.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            HashSet<string>? common = null;
            foreach (var route in g)
            {
                if (common is null) common = new HashSet<string>(route.Segments, StringComparer.OrdinalIgnoreCase);
                else common.IntersectWith(route.Segments);
            }
            EcuChannels[g.Key] = common ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>The home channel(s) of an ECU, or empty if unknown.</summary>
    public HashSet<string> ChannelsOf(string ecu)
        => EcuChannels.TryGetValue((ecu ?? "").Trim(), out var s) ? s : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string Summary =>
        $"Signals: {Signals.Count:N0} ({SignalByName.Count:N0} distinct, {SignalMappingsByName.Count(kv => kv.Value.Count > 1):N0} multi-frame) | "
        + $"Demands: {Demands.Count:N0} | Applied ISRs: {AppliedIsrs.Count:N0} | Dico: {Dico.Count:N0} | Routes: {Routes.Count:N0}";
}

public sealed class ReferenceDataLoader
{
    /// <summary>Load the Message List signal database from an .xlsx. Prefers "(FD+HS) all CAN", falls back to "all PDU".</summary>
    public List<SignalDef> LoadMessageList(string path, IProgress<string>? progress = null)
    {
        progress?.Report("Opening Message List…");
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.FirstOrDefault(w => ColumnMap.Norm(w.Name).Contains("FD+HS"))
              ?? wb.Worksheets.FirstOrDefault(w => ColumnMap.Norm(w.Name).Contains("all PDU"))
              ?? throw new InvalidOperationException("No 'Message List' sheet found.");

        var header = ws.Row(1);
        var m = new ColumnMap(header);
        int cSig = m.Col("Signal Name"), cFrame = m.Col("Frame Name"), cId = m.Col("Frame ID (Hex)", "Frame ID");
        int cContainer = m.Col("Frame Container");
        int cType = m.Col("Frame Type"), cPdu = m.Col("Contained I-PDU Name", "PDU Name");
        int cByte = m.Col("Byte Position in ContainedPDU", "Byte Position", "Start Byte");
        int cBit = m.Col("Bit Position in ContainedPDU", "Bit Position", "Start Bit");
        int cSize = m.Col("Signal Size (Bits)"), cVt = m.Col("Value Type (Sign)", "Value Type");
        int cCode = m.Col("Coding (Bin/Hex)", "Coding"), cMean = m.Col("Meaning"), cUnit = m.Col("Unit");
        int cRes = m.Col("Resolution (Dec)", "Resolution"), cOff = m.Col("Offset (Dec)", "Offset");
        int cMin = m.Col("Min (Dec)", "Min"), cMax = m.Col("Max (Dec)", "Max");
        int cTx = m.Col("Transmission Type"), cPer = m.Col("Period (ms)", "Period"), cExcl = m.Col("Excl. Time (ms)", "Excl. Time");

        // --- ECU node columns: any other header whose data cells contain only T / R marks ---
        var known = new HashSet<int> { cSig, cFrame, cId, cContainer, cType, cPdu, cByte, cBit, cSize, cVt, cCode, cMean, cUnit, cRes, cOff, cMin, cMax, cTx, cPer, cExcl };
        var ecuCols = new List<(int col, string name)>();
        var trSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "T", "R", "T/R", "TR", "T-R" };
        int lastHeaderCol = header.LastCellUsed()?.Address.ColumnNumber ?? 1;
        int probeEnd = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 1, 300);
        for (int c = 1; c <= lastHeaderCol; c++)
        {
            if (known.Contains(c)) continue;
            var hname = ColumnMap.Norm(header.Cell(c).GetString());
            if (hname.Length == 0) continue;
            bool sawValue = false, allTr = true;
            for (int r = 2; r <= probeEnd && allTr; r++)
            {
                var v = ws.Row(r).Cell(c).GetString().Trim();
                if (v.Length == 0) continue;
                sawValue = true;
                if (!trSet.Contains(v)) allTr = false;
            }
            if (sawValue && allTr) ecuCols.Add((c, hname));
        }

        var list = new List<SignalDef>();
        int last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (int r = 2; r <= last; r++)
        {
            var row = ws.Row(r);
            var name = m.Get(row, cSig);
            if (name.Length == 0) continue;
            var def = new SignalDef
            {
                SignalName = name,
                FrameName = m.Get(row, cFrame),
                FrameIdHex = m.Get(row, cId),
                FrameContainer = m.Get(row, cContainer),
                FrameType = m.Get(row, cType),
                PduName = m.Get(row, cPdu),
                BytePosition = m.Get(row, cByte),
                BitPosition = m.Get(row, cBit),
                SignalSizeBits = int.TryParse(m.Get(row, cSize), out var b) ? b : null,
                ValueType = m.Get(row, cVt),
                Coding = m.Get(row, cCode),
                Meaning = m.Get(row, cMean),
                Unit = m.Get(row, cUnit),
                Resolution = m.Get(row, cRes),
                Offset = m.Get(row, cOff),
                Min = m.Get(row, cMin),
                Max = m.Get(row, cMax),
                TransmissionType = m.Get(row, cTx),
                Period = m.Get(row, cPer),
                ExclTime = m.Get(row, cExcl),
            };
            foreach (var (col, ecuName) in ecuCols)
            {
                var v = row.Cell(col).GetString().Trim();
                if (v.Length > 0) def.EcuTxRx[ecuName] = v;
            }
            list.Add(def);
            if (r % 2000 == 0) progress?.Report($"Message List… {r:N0} rows");
        }
        return list;
    }

    public List<AppliedIsr> LoadAppliedIsrs(IXLWorkbook wb)
    {
        var ws = wb.Worksheets.FirstOrDefault(w => ColumnMap.Norm(w.Name).Contains("applied"))
              ?? wb.Worksheets.FirstOrDefault(w => new ColumnMap(w.Row(1)).Col("ISR N°", "ISR_Number", "ISR Number") > 0)
              ?? wb.Worksheets.FirstOrDefault();
        var list = new List<AppliedIsr>();
        if (ws is null) return list;
        var m = new ColumnMap(ws.Row(1));
        int cIsr = m.Col("ISR N°", "ISR No"), cFeat = m.Col("Electronic Feature", "Feature");
        int cTx = m.Col("Transmitter"), cRx = m.Col("Receiver"), cFrame = m.Col("Frame");
        int cParam = m.Col("Parameter"), cAsil = m.Col("ASIL Level"), cClk = m.Col("CLK"), cCrc = m.Col("CRC");
        int last = ws.LastRowUsed()?.RowNumber() ?? 1;
        int lastCol = ws.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 1;
        for (int r = 2; r <= last; r++)
        {
            var row = ws.Row(r);
            var isr = m.Get(row, cIsr);
            if (isr.Length == 0) continue;
            list.Add(new AppliedIsr
            {
                IsrNumber = isr,
                Feature = m.Get(row, cFeat),
                Transmitter = m.Get(row, cTx),
                Receiver = m.Get(row, cRx),
                Frame = m.Get(row, cFrame),
                Parameter = m.Get(row, cParam),
                AsilLevel = m.Get(row, cAsil),
                Clk = m.Get(row, cClk),
                Crc = m.Get(row, cCrc),
                LatestStatus = row.Cell(lastCol).GetString().Trim(),
            });
        }
        return list;
    }

    public List<EcuDicoEntry> LoadDico(IXLWorkbook wb)
    {
        var ws = wb.Worksheets.FirstOrDefault(w => ColumnMap.Norm(w.Name) == "Dico");
        var list = new List<EcuDicoEntry>();
        if (ws is null) return list;
        var m = new ColumnMap(ws.Row(1));
        int cName = m.Col("ECU Msg Set"), cCode = m.Col("Code");
        int cStatus = m.Col("ISR Status Name"), cDiff = m.Col("Different");
        int last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (int r = 2; r <= last; r++)
        {
            var row = ws.Row(r);
            var name = m.Get(row, cName);
            if (name.Length == 0) continue;
            list.Add(new EcuDicoEntry
            {
                Name = name,
                Code = m.Get(row, cCode),
                IsrStatusName = m.Get(row, cStatus),
                Different = m.Get(row, cDiff).Equals("yes", StringComparison.OrdinalIgnoreCase),
            });
        }
        return list;
    }

    public List<NetworkRoute> LoadRoutes(IXLWorkbook wb)
    {
        var ws = wb.Worksheets.FirstOrDefault(w => ColumnMap.Norm(w.Name).Contains("Network Path"));
        var list = new List<NetworkRoute>();
        if (ws is null) return list;
        var header = ws.Row(1);
        var m = new ColumnMap(header);
        int cPdu = m.Col("PDU Name"), cFrame = m.Col("Frame Name"), cTx = m.Col("Transmitter");
        int cRx = m.Col("Receiver"), cSyn = m.Col("Synthesis");

        // Per-segment 'x' columns = every other header between the keys and Synthesis.
        var known = new HashSet<int> { cPdu, cFrame, cTx, cRx, cSyn };
        var segCols = new List<(int col, string name)>();
        int lastCol = header.LastCellUsed()?.Address.ColumnNumber ?? 1;
        for (int c = 1; c <= lastCol; c++)
        {
            if (known.Contains(c)) continue;
            var hname = ColumnMap.Norm(header.Cell(c).GetString());
            if (hname.Length > 0) segCols.Add((c, hname));
        }

        int last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (int r = 2; r <= last; r++)
        {
            var row = ws.Row(r);
            var pdu = m.Get(row, cPdu);
            if (pdu.Length == 0) continue;
            var route = new NetworkRoute
            {
                PduName = pdu, FrameName = m.Get(row, cFrame),
                Transmitter = m.Get(row, cTx), Receiver = m.Get(row, cRx),
                SynthesisPath = m.Get(row, cSyn),
            };
            foreach (var (col, name) in segCols)
                if (row.Cell(col).GetString().Trim().Equals("x", StringComparison.OrdinalIgnoreCase))
                    route.Segments.Add(name);
            list.Add(route);
        }
        return list;
    }

    /// <summary>Load the "Construction of Container frame" assembly layer (header is on row 4 in the FACE workbook).</summary>
    public List<ContainerFrame> LoadContainers(IXLWorkbook wb)
    {
        var ws = wb.Worksheets.FirstOrDefault(w => ColumnMap.Norm(w.Name).Contains("Construction"))
              ?? wb.Worksheets.FirstOrDefault(w => ColumnMap.Norm(w.Name).Contains("Container frame"));
        var list = new List<ContainerFrame>();
        if (ws is null) return list;

        int headerRow = 4;                                  // FACE layout
        var m = new ColumnMap(ws.Row(headerRow));
        if (m.Col("frame name", "Frame Name") == 0) { headerRow = 1; m = new ColumnMap(ws.Row(1)); }

        int cName = m.Col("frame name", "Frame Name"), cId = m.Col("ID", "Frame ID"), cType = m.Col("Frame Type");
        int cTx = m.Col("Tx unit"), cMac = m.Col("MAC"), cTt = m.Col("Transmission Type");
        int cPer = m.Col("Period"), cExcl = m.Col("Excl. Time", "Excl Time");
        int cOrigId = m.Col("original ID"), cPdu = m.Col("Contained I-PDU name", "Contained I-PDU Name");
        int cOrigLen = m.Col("original Length"), cOrigTt = m.Col("original TransmissionT", "original Transmission Type");
        int cOrigPer = m.Col("original Period"), cOrigExcl = m.Col("original Excl. Time", "original Excl Time");
        int cOrigTx = m.Col("original Tx unit");

        int last = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        for (int r = headerRow + 1; r <= last; r++)
        {
            var row = ws.Row(r);
            var name = m.Get(row, cName);
            var pdu = m.Get(row, cPdu);
            if (name.Length == 0 && pdu.Length == 0) continue;
            list.Add(new ContainerFrame
            {
                FrameName = name, FrameId = m.Get(row, cId), FrameType = m.Get(row, cType),
                TxUnit = m.Get(row, cTx), Mac = m.Get(row, cMac), TransmissionType = m.Get(row, cTt),
                Period = m.Get(row, cPer), ExclTime = m.Get(row, cExcl),
                OriginalId = m.Get(row, cOrigId), ContainedPdu = pdu,
                OriginalLength = m.Get(row, cOrigLen), OriginalTransmissionType = m.Get(row, cOrigTt),
                OriginalPeriod = m.Get(row, cOrigPer), OriginalExclTime = m.Get(row, cOrigExcl),
                OriginalTxUnit = m.Get(row, cOrigTx),
            });
        }
        return list;
    }

    /// <summary>FACE checklist step: replace ECU names to match the msg set.</summary>
    private static readonly Dictionary<string, string> EcuNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PIU_Mst"] = "PIU_MASTER", ["PIU_Hood"] = "PIU_HOOD", ["PIU_Sub"] = "PIU_SUB",
    };

    private static string NormEcu(string name)
        => EcuNameMap.TryGetValue((name ?? "").Trim(), out var v) ? v : (name ?? "").Trim();

    /// <summary>Load the incoming ISR demands from the main "ExchangeFile" sheet (exact match, not the _L3/_Diff variants).</summary>
    public List<IsrDemand> LoadDemands(IXLWorkbook wb)
    {
        var ws = wb.Worksheets.FirstOrDefault(w => ColumnMap.Norm(w.Name).Equals("ExchangeFile", StringComparison.OrdinalIgnoreCase));
        var list = new List<IsrDemand>();
        if (ws is null) return list;
        var m = new ColumnMap(ws.Row(1));
        int cIsr = m.Col("ISR_Number"), cFeat = m.Col("Feature_Number");
        int cEC = m.Col("EmitterCode"), cE = m.Col("Emitter"), cRC = m.Col("ReceiverCode"), cR = m.Col("Receiver");
        int cFrame = m.Col("Frame"), cParam = m.Col("ParameterProposal", "Parameter");
        int cMedia = m.Col("Media Type"), cNet = m.Col("NetworkType"), cSyn = m.Col("Synthesis_Status");
        int cUpd = m.Col("UpdateTime"), cLog = m.Col("LogicalData"), cAna = m.Col("AnalogData"), cKind = m.Col("KindOfIsr");
        int cOther = m.Col("OtherRequirements", "Other Requirements");
        int last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (int r = 2; r <= last; r++)
        {
            var row = ws.Row(r);
            var isr = m.Get(row, cIsr);
            var param = m.Get(row, cParam);
            if (isr.Length == 0 && param.Length == 0) continue;
            var demand = new IsrDemand
            {
                IsrNumber = isr, FeatureNumber = m.Get(row, cFeat),
                EmitterCode = m.Get(row, cEC), Emitter = NormEcu(m.Get(row, cE)),
                ReceiverCode = m.Get(row, cRC), Receiver = NormEcu(m.Get(row, cR)),
                Frame = m.Get(row, cFrame), ParameterProposal = param,
                MediaType = m.Get(row, cMedia), NetworkType = m.Get(row, cNet),
                SynthesisStatus = m.Get(row, cSyn), UpdateTime = m.Get(row, cUpd),
                LogicalData = m.Get(row, cLog), AnalogData = m.Get(row, cAna),
                KindOfIsr = m.Get(row, cKind), OtherRequirements = m.Get(row, cOther),
            };
            OtherRequirementsParser.Apply(demand);
            list.Add(demand);
        }
        return list;
    }

    /// <summary>Just the signal names from a (second-architecture) Message List, for the Level 2.1 lookup.</summary>
    public HashSet<string> LoadSignalNames(string messageListPath)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in LoadMessageList(messageListPath))
            set.Add(s.SignalName);
        return set;
    }

    /// <summary>Full signal defs keyed by name from a (second-architecture) Message List, for Level 2.1 + fill.</summary>
    public Dictionary<string, SignalDef> LoadSignalDefsByName(string messageListPath)
    {
        var dict = new Dictionary<string, SignalDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in LoadMessageList(messageListPath))
            dict.TryAdd(s.SignalName, s);
        return dict;
    }

    /// <summary>v2: three separate inputs — Exchange File (demands), Msg-Set (Message List + Network Path + Dico),
    /// and a standalone ISR-Applied file. Headers are unchanged; only the file boundaries moved.</summary>
    public ReferenceData LoadV2(string exchangeFilePath, string msgSetPath, string isrAppliedPath, IProgress<string>? progress = null)
    {
        var data = new ReferenceData();

        progress?.Report("Loading Message List (msg set)…");
        data.Signals.AddRange(LoadMessageList(msgSetPath, progress));
        data.IndexSignals();   // signal → all frame mappings (keeps duplicates; fillers excluded)

        using (var msg = new XLWorkbook(msgSetPath))
        {
            data.Dico.AddRange(LoadDico(msg));            // Dico ships with the msg set
            data.Routes.AddRange(LoadRoutes(msg));        // Network Path ships with the msg set
            data.Containers.AddRange(LoadContainers(msg));// Construction of Container frame (assembly layer)
            data.IndexContainers();
        }
        data.IndexChannels();   // ECU → home channel, from Network Path segments

        progress?.Report("Loading ISR-Applied…");
        using (var appliedWb = new XLWorkbook(isrAppliedPath))
            data.AppliedIsrs.AddRange(LoadAppliedIsrs(appliedWb));

        progress?.Report("Loading Exchange File demands…");
        using (var exWb = new XLWorkbook(exchangeFilePath))
            data.Demands.AddRange(LoadDemands(exWb));

        progress?.Report("Done. " + data.Summary);
        return data;
    }

    /// <summary>Loads everything that lives inside the Exchange File workbook (demands, applied ISRs, Dico, Network Path).</summary>
    public ReferenceData LoadFromExchangeFile(string exchangeFilePath, string messageListPath, IProgress<string>? progress = null)
    {
        var data = new ReferenceData();
        data.Signals.AddRange(LoadMessageList(messageListPath, progress));
        data.IndexSignals();

        progress?.Report("Opening Exchange File…");
        using var wb = new XLWorkbook(exchangeFilePath);   // .xlsm opens fine with ClosedXML (macros ignored)
        data.Demands.AddRange(LoadDemands(wb));
        data.AppliedIsrs.AddRange(LoadAppliedIsrs(wb));
        data.Dico.AddRange(LoadDico(wb));
        data.Routes.AddRange(LoadRoutes(wb));
        progress?.Report("Done. " + data.Summary);
        return data;
    }
}
