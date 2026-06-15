using ClosedXML.Excel;
using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Services;

/// <summary>
/// Phase 4 — exports the demands to an Alliance-style import sheet, with distinct **270** (full) and
/// **212** (reduced) column layouts and a **"Ready to import"** gate column.
///
/// PROVISIONAL: the exact per-format column order/headers and the real gate column come from a real
/// Alliance export. The two layouts below are explicit and distinct (270 = full superset incl. filled
/// frame/PDU/bits/CRC-CLK + the parsed data cells; 212 = the core subset), and the gate is derived from
/// validation (no Error ⇒ "Ready to import"). Send a real 270/212 sample and these lock to it exactly.
/// </summary>
public sealed class ExportService
{
    private sealed record Col(string Header, Func<IsrDemand, string> Get, bool Text = false);

    private static string FrameOf(IsrDemand d) => string.IsNullOrEmpty(d.FilledFrame) ? d.Frame : d.FilledFrame;

    // Shared building blocks (Text=true → stored as text so leading zeros / long numbers survive).
    private static readonly Col CIsr = new("ISR_Number", d => d.IsrNumber);
    private static readonly Col CFeat = new("Feature_Number", d => d.FeatureNumber, Text: true);
    private static readonly Col CEmCode = new("EmitterCode", d => d.EmitterCode, Text: true);
    private static readonly Col CEmit = new("Emitter", d => d.Emitter);
    private static readonly Col CRxCode = new("ReceiverCode", d => d.ReceiverCode, Text: true);
    private static readonly Col CRecv = new("Receiver", d => d.Receiver);
    private static readonly Col CFrame = new("Frame", FrameOf);
    private static readonly Col CParam = new("Parameter", d => d.ParameterProposal);
    private static readonly Col CMedia = new("Media Type", d => d.MediaType);
    private static readonly Col CNet = new("NetworkType", d => d.NetworkType);
    private static readonly Col CSyn = new("Synthesis_Status", d => d.SynthesisStatus);
    private static readonly Col CUpd = new("UpdateTime", d => d.UpdateTime);
    private static readonly Col CLevel = new("Level", d => d.LevelLabel);
    private static readonly Col CReady = new("Ready to import", d => d.ImportStatus);

    /// <summary>270 = full layout (adds frame-id / PDU / bits / value-type / CRC-CLK / data cells / other-req).</summary>
    private static readonly Col[] Layout270 =
    {
        CIsr, CFeat, CEmCode, CEmit, CRxCode, CRecv,
        CFrame,
        new("Frame ID", d => d.FilledFrameId),
        new("PDU", d => d.FilledPdu),
        CParam,
        new("Signal Size (Bits)", d => d.FilledBits?.ToString() ?? ""),
        new("Value Type", d => d.FilledValueType),
        CMedia, CNet, CSyn, CUpd,
        new("LogicalData", d => d.LogicalData),
        new("AnalogData", d => d.AnalogData),
        new("KindOfIsr", d => d.KindOfIsr),
        new("OtherRequirements", d => d.OtherRequirements),
        new("LossLinkageASIL", d => d.LossLinkageAsil),
        new("CorruptDataASIL", d => d.CorruptDataAsil),
        new("CRC/CLK", d => d.CrcClk),
        CLevel, CReady,
    };

    /// <summary>212 = reduced core layout.</summary>
    private static readonly Col[] Layout212 =
    {
        CIsr, CFeat, CEmCode, CEmit, CRxCode, CRecv,
        CFrame, CParam, CMedia, CNet, CSyn, CUpd,
        CLevel, CReady,
    };

    private static Col[] LayoutFor(string format) => format == "212" ? Layout212 : Layout270;

    public int Export(IEnumerable<IsrDemand> demands, string path, string format, bool readyOnly)
    {
        var cols = LayoutFor(format);
        var rows = demands
            .Where(d => !readyOnly || d.ImportStatus.Equals("Ready to import", StringComparison.OrdinalIgnoreCase))
            .ToList();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet($"Import_{format}");

        for (int c = 0; c < cols.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = cols[c].Header;
            cell.Style.Font.Bold = true;
            if (cols[c].Text)
                ws.Column(c + 1).Style.NumberFormat.Format = "@";   // force text
        }

        int r = 2;
        foreach (var d in rows)
        {
            for (int c = 0; c < cols.Length; c++)
                ws.Cell(r, c + 1).Value = cols[c].Get(d) ?? "";   // Text columns kept as text by the "@" column format
            r++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
        return rows.Count;
    }

    public static int ColumnCount(string format) => LayoutFor(format).Length;

    public static string SuggestFileName(string format, string arch = "C1AHS")
        => $"Preevision_ISR_Import_{format}_{arch}_{DateTime.Now:dd_MM_yyyy}.xlsx";

    public void ExportConsolidatedExchangeFile(string originalExchangeFilePath, string targetPath, IEnumerable<IsrDemand> demands, ReferenceData data)
    {
        // 1. Copy original file to targetPath
        System.IO.File.Copy(originalExchangeFilePath, targetPath, overwrite: true);

        // 2. Open targetPath using ClosedXML
        using var wb = new XLWorkbook(targetPath);
        var ws = wb.Worksheet("ExchangeFile");
        if (ws is null) throw new InvalidOperationException("Sheet 'ExchangeFile' not found in consolidated workbook.");

        // Clear AutoFilters on all worksheets to prevent ClosedXML PopulateAutoFilter System.NotSupportedException during Save
        foreach (var wsheet in wb.Worksheets)
        {
            try { wsheet.AutoFilter.Clear(); } catch { }
        }

        // 3. Map headers of sheet ExchangeFile
        var header = ws.Row(1);
        var m = new ColumnMap(header);

        int cLevel = m.Col("ISR Level", "ISR_Level");
        int cComment = m.Col("Comment");
        int cSigName = m.Col("Signal Name", "Signal_Name");
        int cSigSize = m.Col("Signal Size (Bits)", "Signal Size(Bits)");
        int cUnit = m.Col("Unit");
        int cRes = m.Col("Resolution(Dec)", "Resolution");
        int cOffset = m.Col("Offset(Dec)", "Offset");
        int cMin = m.Col("Min(Dec)", "Min");
        int cMax = m.Col("Max(Dec)", "Max");
        int cUnavail = m.Col("Unavailable Value(Bin/ Hex)", "Unavailable Value (Bin/Hex)", "Unavailable Value");
        int cCoding = m.Col("Coding (Bin / Hex)", "Coding (Bin/Hex)", "Coding");
        int cMeaning = m.Col("Meaning");
        int cPduName = m.Col("Frame Name (PDU Name)", "PDU Name");
        int cFrameId = m.Col("Frame ID (Hex)", "Frame ID");
        int cFrameType = m.Col("Frame type", "Frame Type");
        int cTxType = m.Col("Transmission Type");
        int cPeriod = m.Col("Period (ms)", "Period");
        int cExcl = m.Col("Excl. Time (ms)", "Excl. Time");
        int cProtocol = m.Col("Protocol");
        int cEvent = m.Col("Event");
        int cClock = m.Col("Clock?");
        int cCrc = m.Col("CRC?");
        int cContName = m.Col("Conteiner needs or not");
        int cNetPath = m.Col("[NetworkPath]", "NetworkPath");
        int cStatus = m.Col("Staus (Other Requirment and Meaning)", "Status (Other Requirement and Meaning)");
        int cImportStatus = m.Col("PV: Import Status", "Import Status");
        int cBytePos = m.Col("Byte Position HSevo (0 to 7)", "Byte Position");
        int cBitPos = m.Col("Bit Position HSevo (7 to 0)", "Bit Position");

        // 4. Map demands by ISR_Number and ParameterProposal to quickly find them
        var demandsMap = new System.Collections.Generic.Dictionary<string, IsrDemand>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var d in demands)
        {
            var key = $"{d.IsrNumber.Trim()}|{(d.ParameterProposal ?? "").Trim()}";
            demandsMap[key] = d;
        }

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        int cIsrCol = m.Col("ISR_Number");
        int cParamCol = m.Col("ParameterProposal", "Parameter");

        for (int r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            var isr = row.Cell(cIsrCol).GetString().Trim();
            var param = row.Cell(cParamCol).GetString().Trim();
            var key = $"{isr}|{param}";

            if (demandsMap.TryGetValue(key, out var d))
            {
                var sig = d.MatchedDef;
                var cont = sig is null ? null : FrameTraceService.ResolveContainer(sig, data);
                var route = FrameTraceService.ResolveRoute(d, sig, cont, data);

                if (cLevel > 0) row.Cell(cLevel).Value = d.LevelLabel;
                if (cComment > 0) row.Cell(cComment).Value = d.Note;
                if (cSigName > 0) row.Cell(cSigName).Value = d.ParameterProposal;
                if (cSigSize > 0) row.Cell(cSigSize).Value = d.FilledBits?.ToString() ?? "";
                if (cUnit > 0) row.Cell(cUnit).Value = d.FilledUnit;
                if (cRes > 0 && sig is not null) row.Cell(cRes).Value = sig.Resolution;
                if (cOffset > 0 && sig is not null) row.Cell(cOffset).Value = sig.Offset;
                if (cMin > 0) row.Cell(cMin).Value = d.FilledMin;
                if (cMax > 0) row.Cell(cMax).Value = d.FilledMax;
                if (cUnavail > 0 && sig is not null) row.Cell(cUnavail).Value = sig.UnavailableValue;
                if (cCoding > 0 && sig is not null) row.Cell(cCoding).Value = sig.Coding;
                if (cMeaning > 0 && sig is not null) row.Cell(cMeaning).Value = sig.Meaning;
                if (cPduName > 0) row.Cell(cPduName).Value = d.FilledPdu;
                if (cFrameId > 0) row.Cell(cFrameId).Value = d.FilledFrameId;
                if (cFrameType > 0 && sig is not null) row.Cell(cFrameType).Value = sig.FrameType;
                if (cTxType > 0 && sig is not null) row.Cell(cTxType).Value = sig.TransmissionType;
                if (cPeriod > 0 && sig is not null) row.Cell(cPeriod).Value = sig.Period;
                if (cExcl > 0 && sig is not null) row.Cell(cExcl).Value = sig.ExclTime;
                if (cProtocol > 0 && sig is not null) row.Cell(cProtocol).Value = sig.Protocol;
                if (cEvent > 0 && sig is not null) row.Cell(cEvent).Value = sig.Event;
                if (cClock > 0) row.Cell(cClock).Value = d.HasClkOnFrame ? "x" : "";
                if (cCrc > 0) row.Cell(cCrc).Value = d.HasCrcOnFrame ? "x" : "";
                if (cContName > 0 && cont is not null) row.Cell(cContName).Value = cont.FrameName;
                if (cNetPath > 0) row.Cell(cNetPath).Value = route?.SynthesisPath ?? "";
                if (cStatus > 0) row.Cell(cStatus).Value = d.FillStatus;
                if (cImportStatus > 0) row.Cell(cImportStatus).Value = d.ImportStatus;
                if (cBytePos > 0 && sig is not null) row.Cell(cBytePos).Value = sig.BytePosition;
                if (cBitPos > 0 && sig is not null) row.Cell(cBitPos).Value = sig.BitPosition;
            }
        }
        wb.Save();
    }
}
