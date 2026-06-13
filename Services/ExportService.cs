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
}
