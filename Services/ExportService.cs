using ClosedXML.Excel;
using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Services;

/// <summary>
/// Phase 4 — exports the demands to an Alliance-style import sheet (212 / 270).
///
/// NOTE: the macro gates export on an "Import 270 = Ready to import" column and has a
/// precise column layout per format. We don't carry that gate column yet, so export can
/// be "all" or "ready only" (ImportStatus == "Ready to import"). The column set below is a
/// sensible superset — confirm against a real Alliance export and tell me the exact 212/270
/// column differences and I'll lock them in.
/// </summary>
public sealed class ExportService
{
    private static readonly string[] Headers =
    {
        "ISR_Number", "Feature_Number", "EmitterCode", "Emitter", "ReceiverCode", "Receiver",
        "Frame", "Parameter", "Media Type", "NetworkType", "Synthesis_Status", "UpdateTime", "Level"
    };

    // columns that must be stored as text so leading zeros / long numbers survive
    private static readonly HashSet<string> TextCols = new(StringComparer.OrdinalIgnoreCase)
        { "Feature_Number", "EmitterCode", "ReceiverCode" };

    public int Export(IEnumerable<IsrDemand> demands, string path, string format, bool readyOnly)
    {
        var rows = demands
            .Where(d => !readyOnly || d.ImportStatus.Equals("Ready to import", StringComparison.OrdinalIgnoreCase))
            .ToList();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet($"Import_{format}");

        for (int c = 0; c < Headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = Headers[c];
            cell.Style.Font.Bold = true;
            if (TextCols.Contains(Headers[c]))
                ws.Column(c + 1).Style.NumberFormat.Format = "@";
        }

        int r = 2;
        foreach (var d in rows)
        {
            var vals = new[]
            {
                d.IsrNumber, d.FeatureNumber, d.EmitterCode, d.Emitter, d.ReceiverCode, d.Receiver,
                string.IsNullOrEmpty(d.FilledFrame) ? d.Frame : d.FilledFrame,
                d.ParameterProposal, d.MediaType, d.NetworkType, d.SynthesisStatus, d.UpdateTime,
                d.LevelLabel
            };
            for (int c = 0; c < vals.Length; c++)
                ws.Cell(r, c + 1).Value = vals[c] ?? "";
            r++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
        return rows.Count;
    }

    public static string SuggestFileName(string format, string arch = "C1AHS")
        => $"Preevision_ISR_Import_{format}_{arch}_{DateTime.Now:dd_MM_yyyy}.xlsx";
}
