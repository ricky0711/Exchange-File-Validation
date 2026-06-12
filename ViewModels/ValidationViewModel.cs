using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeFileValidator.Models;
using ExchangeFileValidator.Services;

namespace ExchangeFileValidator.ViewModels;

public partial class ValidationViewModel : ObservableObject
{
    private ReferenceData? _data;

    public ValidationViewModel()
    {
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = Filter;
    }

    public ObservableCollection<ValidationRow> Rows { get; } = new();
    public ICollectionView RowsView { get; }

    /// <summary>Set by MainViewModel; the Run button asks it to re-run the whole suite.</summary>
    public Action? OnRerun { get; set; }

    public string[] SeverityFilters { get; } = { "All", "Error", "Warning", "Info" };
    public string[] SearchFields { get; } = { "All", "Signal", "ISR", "Rule", "Message" };
    public ObservableCollection<string> RuleFilters { get; } = new() { "All" };

    [ObservableProperty] private string _selectedSeverity = "All";
    [ObservableProperty] private string _selectedRule = "All";
    [ObservableProperty] private string _selectedSearchField = "All";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _summary = "Load data to validate.";

    /// <summary>Rebuild the findings grid from the demands (already validated by MainViewModel).</summary>
    public void SetData(ReferenceData data)
    {
        _data = data;
        var rows = ValidationService.ToRows(data.Demands);
        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);

        var rules = rows.Select(r => r.Rule).Distinct().OrderBy(x => x).ToList();
        RuleFilters.Clear(); RuleFilters.Add("All");
        foreach (var r in rules) RuleFilters.Add(r);
        if (!RuleFilters.Contains(SelectedRule)) SelectedRule = "All";

        RowsView.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var vis = RowsView.Cast<ValidationRow>().ToList();
        int e = vis.Count(r => r.Severity == Severity.Error);
        int w = vis.Count(r => r.Severity == Severity.Warning);
        Summary = $"{vis.Count} findings (filtered)  •  {e} errors  {w} warnings";
    }

    [RelayCommand] private void Run() => OnRerun?.Invoke();

    /// <summary>Focus the grid on one rule + severity (used by the checklist "jump to findings").</summary>
    public void FocusOn(string rule, string severity)
    {
        Search = "";
        SelectedSeverity = SeverityFilters.Contains(severity) ? severity : "All";
        SelectedRule = RuleFilters.Contains(rule) ? rule : "All";
        RowsView.Refresh();
        UpdateSummary();
    }

    private bool Filter(object obj)
    {
        if (obj is not ValidationRow r) return false;
        if (SelectedSeverity != "All" && r.Severity.ToString() != SelectedSeverity) return false;
        if (SelectedRule != "All" && r.Rule != SelectedRule) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;
        var q = Search.Trim();
        bool In(string f) => (f ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
        return SelectedSearchField switch
        {
            "Signal" => In(r.Signal), "ISR" => In(r.Isr),
            "Rule" => In(r.Rule), "Message" => In(r.Message),
            _ => In(r.Signal) || In(r.Isr) || In(r.Rule) || In(r.Message),
        };
    }

    partial void OnSelectedSeverityChanged(string value) { RowsView.Refresh(); UpdateSummary(); }
    partial void OnSelectedRuleChanged(string value) { RowsView.Refresh(); UpdateSummary(); }
    partial void OnSelectedSearchFieldChanged(string value) { RowsView.Refresh(); UpdateSummary(); }
    partial void OnSearchChanged(string value) { RowsView.Refresh(); UpdateSummary(); }
}
