using ClosedXML.Excel;
using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Services;

/// <summary>Writes the full validation report: Checklist + Findings + Demands overview.</summary>
public sealed class ReportExportService
{
    public void Export(string path, IReadOnlyList<CheckSummary> checklist, IReadOnlyList<IsrDemand> demands)
    {
        using var wb = new XLWorkbook();

        // Sheet 1: checklist
        var ws1 = wb.AddWorksheet("Checklist");
        string[] h1 = { "Section", "Check", "Kind", "Status", "Checked", "Passed", "Warnings", "Errors" };
        for (int c = 0; c < h1.Length; c++) { ws1.Cell(1, c + 1).Value = h1[c]; ws1.Cell(1, c + 1).Style.Font.Bold = true; }
        int r = 2;
        foreach (var s in checklist)
        {
            ws1.Cell(r, 1).Value = s.Section; ws1.Cell(r, 2).Value = s.Name; ws1.Cell(r, 3).Value = s.Kind;
            ws1.Cell(r, 4).Value = s.Status; ws1.Cell(r, 5).Value = s.Checked; ws1.Cell(r, 6).Value = s.Passed;
            ws1.Cell(r, 7).Value = s.Warned; ws1.Cell(r, 8).Value = s.Errored;
            ws1.Cell(r, 4).Style.Fill.BackgroundColor = s.Status switch
            {
                "Pass" => XLColor.FromHtml("#C8E6C9"),
                "Warning" => XLColor.FromHtml("#FFE0B2"),
                "Error" => XLColor.FromHtml("#FFCDD2"),
                _ => XLColor.FromHtml("#EEEEEE")
            };
            r++;
        }
        ws1.Columns().AdjustToContents();

        // Sheet 2: findings
        var ws2 = wb.AddWorksheet("Findings");
        string[] h2 = { "Severity", "Rule", "ISR", "Signal", "Message" };
        for (int c = 0; c < h2.Length; c++) { ws2.Cell(1, c + 1).Value = h2[c]; ws2.Cell(1, c + 1).Style.Font.Bold = true; }
        r = 2;
        foreach (var f in ValidationService.ToRows(demands))
        {
            ws2.Cell(r, 1).Value = f.Severity.ToString(); ws2.Cell(r, 2).Value = f.Rule;
            ws2.Cell(r, 3).Value = f.Isr; ws2.Cell(r, 4).Value = f.Signal; ws2.Cell(r, 5).Value = f.Message;
            ws2.Cell(r, 1).Style.Fill.BackgroundColor = f.Severity switch
            {
                Severity.Error => XLColor.FromHtml("#FFCDD2"),
                Severity.Warning => XLColor.FromHtml("#FFE0B2"),
                _ => XLColor.FromHtml("#BBDEFB")
            };
            r++;
        }
        ws2.Columns().AdjustToContents();

        // Sheet 3: demands overview
        var ws3 = wb.AddWorksheet("Demands");
        string[] h3 = { "Level", "ISR", "Signal", "Tx", "Rx", "Frame", "PDU", "Bits", "CRC-CLK note", "Worst", "Issues" };
        for (int c = 0; c < h3.Length; c++) { ws3.Cell(1, c + 1).Value = h3[c]; ws3.Cell(1, c + 1).Style.Font.Bold = true; }
        r = 2;
        foreach (var d in demands)
        {
            ws3.Cell(r, 1).Value = d.LevelLabel; ws3.Cell(r, 2).Value = d.IsrNumber; ws3.Cell(r, 3).Value = d.ParameterProposal;
            ws3.Cell(r, 4).Value = d.Emitter; ws3.Cell(r, 5).Value = d.Receiver;
            ws3.Cell(r, 6).Value = d.FilledFrame; ws3.Cell(r, 7).Value = d.FilledPdu;
            ws3.Cell(r, 8).Value = d.FilledBits?.ToString() ?? ""; ws3.Cell(r, 9).Value = d.CrcNote;
            ws3.Cell(r, 10).Value = d.WorstSeverity?.ToString() ?? "Clean"; ws3.Cell(r, 11).Value = d.Issues;
            r++;
        }
        ws3.Columns().AdjustToContents();

        wb.SaveAs(path);
    }
}
