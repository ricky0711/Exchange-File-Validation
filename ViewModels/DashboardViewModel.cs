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

    [ObservableProperty] private int _totalDemands;
    [ObservableProperty] private int _l0Count;
    [ObservableProperty] private int _l1Count;
    [ObservableProperty] private int _l2Count;
    [ObservableProperty] private int _l21Count;
    [ObservableProperty] private int _l3Count;
    [ObservableProperty] private double _l0Percent;
    [ObservableProperty] private double _l1Percent;
    [ObservableProperty] private double _l2Percent;
    [ObservableProperty] private double _l21Percent;
    [ObservableProperty] private double _l3Percent;
    [ObservableProperty] private double _healthPercent;
    [ObservableProperty] private string _healthStatus = "Good";
    [ObservableProperty] private string _healthColor = "#2E9E5B";
    [ObservableProperty] private double _checksPassPercent;
    [ObservableProperty] private bool _isDataLoaded;
    [ObservableProperty] private bool _hasRecentFindings;
    
    public ObservableCollection<ValidationRow> RecentFindings { get; } = new();
    public Action<ValidationRow>? OnSelectFinding { get; set; }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SelectFinding(ValidationRow? row)
    {
        if (row != null) OnSelectFinding?.Invoke(row);
    }

    public void Update(ReferenceData data, IReadOnlyList<CheckSummary> checklist)
    {
        Kpis.Clear();
        var d = data.Demands;
        TotalDemands = d.Count;
        IsDataLoaded = TotalDemands > 0;
        if (TotalDemands == 0) return;

        var lv = LevelAssignmentService.Counts(d);
        L0Count = lv.L0;
        L1Count = lv.L1;
        L2Count = lv.L2;
        L21Count = lv.L21;
        L3Count = lv.L3;

        L0Percent = (double)L0Count / TotalDemands * 100;
        L1Percent = (double)L1Count / TotalDemands * 100;
        L2Percent = (double)L2Count / TotalDemands * 100;
        L21Percent = (double)L21Count / TotalDemands * 100;
        L3Percent = (double)L3Count / TotalDemands * 100;

        int err = d.Count(x => x.WorstSeverity == Severity.Error);
        int warn = d.Count(x => x.WorstSeverity == Severity.Warning);
        int clean = d.Count(x => x.WorstSeverity is null);
        var fill = PropertyFillService.Counts(d);
        int toolChecks = checklist.Count(c => c.Kind == "Tool" && c.Ran);
        int pass = checklist.Count(c => c.Ran && c.Status == "Pass");

        HealthPercent = (double)clean / TotalDemands * 100;
        HealthStatus = err > 0 ? "Needs Review" : warn > 0 ? "Review Warnings" : "Fully Correct";
        HealthColor = err > 0 ? "#E53935" : warn > 0 ? "#FB8C00" : "#2E9E5B";
        ChecksPassPercent = toolChecks > 0 ? (double)pass / toolChecks * 100 : 100;

        Kpis.Add(new("ISR Demands", d.Count.ToString(), "from the Exchange File", "#3D5AFE"));
        Kpis.Add(new("Levels", $"L0 {lv.L0} · L1 {lv.L1} · L2 {lv.L2}", $"L2.1 {lv.L21} · L3 {lv.L3}", "#6D5DF5"));
        Kpis.Add(new("Clean", clean.ToString(), $"{warn} warning · {err} error", err > 0 ? "#E53935" : warn > 0 ? "#FB8C00" : "#2E9E5B"));
        Kpis.Add(new("Properties filled", fill.filled.ToString(), $"{fill.pending} pending L2.1 · {fill.newSig} new", "#00A4B4"));
        Kpis.Add(new("Checks passed", $"{pass}/{toolChecks}", "see Checklist for detail", "#7E57C2"));
        Kpis.Add(new("Reference", $"{data.Signals.Count:N0}", $"signals · {data.AppliedIsrs.Count:N0} applied", "#F4A300"));

        Headline = err == 0 && warn == 0
            ? "All automated checks clean. Review manual checklist items, then export."
            : $"{err} demands with errors, {warn} with warnings — see Validation.";

        RecentFindings.Clear();
        var allFindings = ValidationService.ToRows(d);
        foreach (var finding in allFindings.Take(5))
        {
            RecentFindings.Add(finding);
        }
        HasRecentFindings = RecentFindings.Count > 0;
    }
}
