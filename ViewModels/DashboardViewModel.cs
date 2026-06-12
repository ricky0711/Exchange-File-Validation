using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ExchangeFileValidator.Models;
using ExchangeFileValidator.Services;

namespace ExchangeFileValidator.ViewModels;

public sealed record Kpi(string Title, string Value, string Detail, string Color);

public partial class DashboardViewModel : ObservableObject
{
    public ObservableCollection<Kpi> Kpis { get; } = new();

    [ObservableProperty] private string _headline = "Load the Message List and Exchange File to begin.";

    public void Update(ReferenceData data, IReadOnlyList<CheckSummary> checklist)
    {
        Kpis.Clear();
        var d = data.Demands;
        var lv = LevelAssignmentService.Counts(d);
        int err = d.Count(x => x.WorstSeverity == Severity.Error);
        int warn = d.Count(x => x.WorstSeverity == Severity.Warning);
        int clean = d.Count(x => x.WorstSeverity is null);
        var fill = PropertyFillService.Counts(d);
        int toolChecks = checklist.Count(c => c.Kind == "Tool" && c.Ran);
        int pass = checklist.Count(c => c.Ran && c.Status == "Pass");

        Kpis.Add(new("ISR Demands", d.Count.ToString(), "from the Exchange File", "#3D5AFE"));
        Kpis.Add(new("Levels", $"L0 {lv.L0} · L1 {lv.L1} · L2 {lv.L2}", $"L2.1 {lv.L21} · L3 {lv.L3}", "#6D5DF5"));
        Kpis.Add(new("Clean", clean.ToString(), $"{warn} warning · {err} error", err > 0 ? "#E53935" : warn > 0 ? "#FB8C00" : "#2E9E5B"));
        Kpis.Add(new("Properties filled", fill.filled.ToString(), $"{fill.pending} pending L2.1 · {fill.newSig} new", "#00A4B4"));
        Kpis.Add(new("Checks passed", $"{pass}/{toolChecks}", "see Checklist for detail", "#7E57C2"));
        Kpis.Add(new("Reference", $"{data.Signals.Count:N0}", $"signals · {data.AppliedIsrs.Count:N0} applied", "#F4A300"));

        Headline = err == 0 && warn == 0
            ? "All automated checks clean. Review manual checklist items, then export."
            : $"{err} demands with errors, {warn} with warnings — see Validation.";
    }
}
