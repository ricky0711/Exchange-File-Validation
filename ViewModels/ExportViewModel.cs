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

    public string[] Formats { get; } = { "270", "212", "Exchange File" };

    [ObservableProperty] private string _selectedFormat = "270";
    [ObservableProperty] private bool _readyOnly;
    [ObservableProperty] private string _exchangeFilePath = "";
    [ObservableProperty] private string _summary = "Load data to enable export.";

    public bool IsReadyOnlyEnabled => SelectedFormat != "Exchange File";

    public void SetData(ReferenceData data) { _data = data; UpdateSummary(); }

    partial void OnReadyOnlyChanged(bool value) => UpdateSummary();
    partial void OnSelectedFormatChanged(string value)
    {
        UpdateSummary();
        OnPropertyChanged(nameof(IsReadyOnlyEnabled));
    }

    private void UpdateSummary()
    {
        if (_data is null) return;
        int total = _data.Demands.Count;
        int ready = _data.Demands.Count(d => d.ImportStatus.Equals("Ready to import", StringComparison.OrdinalIgnoreCase));
        int blocked = total - ready;
        int willExport = ReadyOnly ? ready : total;

        if (SelectedFormat == "Exchange File")
        {
            Summary = $"Format Exchange File (Original Consolidated)  •  {total} demands: {ready} ready, {blocked} blocked (errors)"
                    + $"  •  will fill and export {total} demands";
        }
        else
        {
            int cols = ExportService.ColumnCount(SelectedFormat);
            Summary = $"Format {SelectedFormat} ({cols} columns)  •  {total} demands: {ready} ready, {blocked} blocked (errors)"
                    + $"  •  will export {willExport}" + (ReadyOnly ? "  (ready only)" : "");
        }
    }

    [RelayCommand]
    private void Export()
    {
        if (_data is null) { return; }
        if (SelectedFormat == "Exchange File")
        {
            if (string.IsNullOrEmpty(ExchangeFilePath) || !System.IO.File.Exists(ExchangeFilePath))
            {
                MessageBox.Show("Original Exchange File path is not available or file does not exist.", "Export error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var origExt = System.IO.Path.GetExtension(ExchangeFilePath);
            var dlg = new SaveFileDialog
            {
                Filter = origExt.Equals(".xlsm", StringComparison.OrdinalIgnoreCase) 
                    ? "Excel Macro-Enabled Workbook (*.xlsm)|*.xlsm|Excel Workbook (*.xlsx)|*.xlsx"
                    : "Excel Workbook (*.xlsx)|*.xlsx|Excel Macro-Enabled Workbook (*.xlsm)|*.xlsm",
                FileName = System.IO.Path.GetFileNameWithoutExtension(ExchangeFilePath) + "_Filled" + origExt,
                Title = "Save Filled Exchange File"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _svc.ExportConsolidatedExchangeFile(ExchangeFilePath, dlg.FileName, _data.Demands, _data);
                Summary = $"Filled and saved Exchange File → {System.IO.Path.GetFileName(dlg.FileName)}";
                MessageBox.Show($"Successfully filled and exported Exchange File.", "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
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
}
