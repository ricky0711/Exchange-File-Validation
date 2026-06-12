using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeFileValidator.Models;
using ExchangeFileValidator.Services;
using Microsoft.Win32;

namespace ExchangeFileValidator.ViewModels;

public partial class ExportViewModel : ObservableObject
{
    private readonly ExportService _svc = new();
    private ReferenceData? _data;

    public string[] Formats { get; } = { "270", "212" };

    [ObservableProperty] private string _selectedFormat = "270";
    [ObservableProperty] private bool _readyOnly;
    [ObservableProperty] private string _summary = "Load data to enable export.";

    public void SetData(ReferenceData data) { _data = data; UpdateSummary(); }

    partial void OnReadyOnlyChanged(bool value) => UpdateSummary();
    partial void OnSelectedFormatChanged(string value) => UpdateSummary();

    private void UpdateSummary()
    {
        if (_data is null) return;
        int total = _data.Demands.Count;
        int ready = _data.Demands.Count(d => d.ImportStatus.Equals("Ready to import", StringComparison.OrdinalIgnoreCase));
        int willExport = ReadyOnly ? ready : total;
        Summary = $"Format {SelectedFormat}  •  will export {willExport} of {total} demands"
                + (ReadyOnly ? $"  (only 'Ready to import' = {ready})" : "");
    }

    [RelayCommand]
    private void Export()
    {
        if (_data is null) { return; }
        var dlg = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = ExportService.SuggestFileName(SelectedFormat),
            Title = "Save Alliance import file"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            int n = _svc.Export(_data.Demands, dlg.FileName, SelectedFormat, ReadyOnly);
            Summary = $"Exported {n} demands → {System.IO.Path.GetFileName(dlg.FileName)}";
            MessageBox.Show($"Exported {n} rows.", "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
