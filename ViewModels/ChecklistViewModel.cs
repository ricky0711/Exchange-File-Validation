using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeFileValidator.Models;
using ExchangeFileValidator.Services;
using Microsoft.Win32;

namespace ExchangeFileValidator.ViewModels;

public partial class ChecklistViewModel : ObservableObject
{
    public ChecklistViewModel()
    {
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CheckSummary.Section)));
    }

    public ObservableCollection<CheckSummary> Items { get; } = new();
    public ICollectionView ItemsView { get; }

    /// <summary>Set by MainViewModel; the Run button re-runs the whole suite.</summary>
    public Action? OnRerun { get; set; }
    /// <summary>Set by MainViewModel; provides demands for the report export.</summary>
    public Func<IReadOnlyList<IsrDemand>>? GetDemands { get; set; }

    [ObservableProperty] private string _summary = "Load data to run the checklist.";

    public void SetSummaries(IEnumerable<CheckSummary> summaries)
    {
        Items.Clear();
        foreach (var s in summaries) Items.Add(s);
        int tool = Items.Count(i => i.Kind == "Tool");
        int ran = Items.Count(i => i.Ran);
        int pass = Items.Count(i => i.Ran && i.Status == "Pass");
        int warn = Items.Count(i => i.Ran && i.Status == "Warning");
        int err = Items.Count(i => i.Ran && i.Status == "Error");
        int manual = Items.Count(i => !i.Ran);
        Summary = $"{ran}/{tool} tool checks run  •  {pass} pass, {warn} warning, {err} error  •  {manual} manual/external";
        ItemsView.Refresh();
    }

    [RelayCommand] private void Run() => OnRerun?.Invoke();

    [RelayCommand]
    private void ExportReport()
    {
        var demands = GetDemands?.Invoke();
        if (demands is null || Items.Count == 0) return;
        var dlg = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"Validation_Report_{DateTime.Now:dd_MM_yyyy}.xlsx",
            Title = "Save validation report"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            new ReportExportService().Export(dlg.FileName, Items.ToList(), demands);
            MessageBox.Show("Report saved.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
