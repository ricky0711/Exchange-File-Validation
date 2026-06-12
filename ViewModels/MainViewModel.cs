using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeFileValidator.Models;
using ExchangeFileValidator.Services;
using Microsoft.Win32;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace ExchangeFileValidator.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ReferenceDataLoader _loader;
    private readonly ValidationService _vsvc = new();
    private readonly LevelAssignmentService _lsvc = new();
    private readonly PropertyFillService _fillSvc = new();
    private readonly IsrDetailBuilder _detailSvc = new();
    private ReferenceData? _data;
    private HashSet<string>? _secondArch;

    public MainViewModel(ReferenceDataLoader loader)
    {
        _loader = loader;
        Dashboard = new DashboardViewModel();
        ReferenceBrowser = new ReferenceBrowserViewModel();
        ExchangeFile = new ExchangeFileViewModel();
        Validation = new ValidationViewModel();
        Checklist = new ChecklistViewModel();
        IsrVsMsgSet = new PropertyCompareViewModel(CompareMode.SignalProperties, "ISR vs Message Set (signal properties)");
        OtherReqCompare = new PropertyCompareViewModel(CompareMode.OtherRequirements, "Other Requirements (Tx / Rx / UV / Network Path)");
        Export = new ExportViewModel();
        _currentPage = Dashboard;

        Checklist.GetDemands = () => _data?.Demands ?? new List<IsrDemand>();

        Validation.OnRerun = RunValidation;
        Checklist.OnRerun = RunValidation;

        // The Exchange File page now owns level/fill re-run (merged from the old Level + Property Fill pages).
        ExchangeFile.OnRerun = RunPipeline;
        ExchangeFile.OnLoadSecondArch = LoadSecondArchitecture;
    }

    public DashboardViewModel Dashboard { get; }
    public ReferenceBrowserViewModel ReferenceBrowser { get; }
    public ExchangeFileViewModel ExchangeFile { get; }
    public ValidationViewModel Validation { get; }
    public ChecklistViewModel Checklist { get; }
    public PropertyCompareViewModel IsrVsMsgSet { get; }
    public PropertyCompareViewModel OtherReqCompare { get; }
    public ExportViewModel Export { get; }

    [ObservableProperty] private object _currentPage;
    [ObservableProperty] private string _exchangeFilePath = "";
    [ObservableProperty] private string _msgSetPath = "";
    [ObservableProperty] private string _isrAppliedPath = "";

    public string[] Architectures { get; } = { "C1A", "C1A-HS", "C1A-HS evo", "N FACE" };
    [ObservableProperty] private string _selectedArchitecture = "C1A-HS";
    [ObservableProperty] private string _status = "Select the Message List (.xlsx) and Exchange File (.xlsm), then Load.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isLoaded;

    [RelayCommand] private void NavDashboard() => CurrentPage = Dashboard;
    [RelayCommand] private void NavReference() => CurrentPage = ReferenceBrowser;
    [RelayCommand] private void NavIsrVsMsgSet() { if (IsLoaded) CurrentPage = IsrVsMsgSet; }
    [RelayCommand] private void NavOtherReq() { if (IsLoaded) CurrentPage = OtherReqCompare; }

    [ObservableProperty] private bool _isDarkTheme = false;
    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        var theme = IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(theme);
        ApplicationAccentColorManager.Apply(
            (Color)ColorConverter.ConvertFromString("#6D5DF5")!, theme);
    }

    [RelayCommand] private void NavExchange() { if (IsLoaded) CurrentPage = ExchangeFile; }
    [RelayCommand] private void NavValidation() { if (IsLoaded) CurrentPage = Validation; }
    [RelayCommand] private void NavChecklist() { if (IsLoaded) CurrentPage = Checklist; }
    [RelayCommand] private void NavExport() { if (IsLoaded) CurrentPage = Export; }

    /// <summary>Assign levels → fill properties → validate → refresh every dependent page.</summary>
    private void RunPipeline()
    {
        if (_data is null) return;
        _lsvc.Assign(_data.Demands, _data, _secondArch);
        _fillSvc.Fill(_data.Demands, _data);
        RunValidation();
        ExchangeFile.UpdatePipelineSummary(_data);
    }

    private void RunValidation()
    {
        if (_data is null) return;
        var summaries = _vsvc.Validate(_data.Demands, _data);
        foreach (var d in _data.Demands) _detailSvc.Build(d, _data);   // side-by-side ISR vs AT detail
        Validation.SetData(_data);
        Checklist.SetSummaries(summaries);
        ExchangeFile.SetData(_data);   // rebuild so row severity tints refresh
        IsrVsMsgSet.SetData(_data);
        OtherReqCompare.SetData(_data);
        Dashboard.Update(_data, summaries);
    }

    /// <summary>Pick a 2nd-architecture Message List (resolves Level 2.1 vs 3), then re-run the pipeline.</summary>
    private void LoadSecondArchitecture()
    {
        if (_data is null) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Message List (*.xlsx)|*.xlsx",
            Title = "Select 2nd architecture Message List (for Level 2.1)"
        };
        if (dlg.ShowDialog() != true) return;
        var defs = _loader.LoadSignalDefsByName(dlg.FileName);
        _data.SecondArchByName = defs;
        _secondArch = new HashSet<string>(defs.Keys, StringComparer.OrdinalIgnoreCase);
        ExchangeFile.SecondArchName = System.IO.Path.GetFileName(dlg.FileName) + $"  ({defs.Count:N0} signals)";
        RunPipeline();
    }

    private static string? Pick(string title, string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter, Title = title };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    [RelayCommand] private void PickExchangeFile() { var f = Pick("Select Exchange File (demands)", "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm"); if (f != null) ExchangeFilePath = f; }
    [RelayCommand] private void PickMsgSet() { var f = Pick("Select Msg-Set / PDU list (Message List + Network Path + Dico)", "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm"); if (f != null) MsgSetPath = f; }
    [RelayCommand] private void PickIsrApplied() { var f = Pick("Select ISR-Applied file", "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm"); if (f != null) IsrAppliedPath = f; }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadAsync()
    {
        IsBusy = true; IsLoaded = false;
        var progress = new Progress<string>(s => Status = s);
        try
        {
            var data = await Task.Run(() => _loader.LoadV2(ExchangeFilePath, MsgSetPath, IsrAppliedPath, progress));
            _data = data;
            _secondArch = data.SecondArchByName is null ? null
                : new HashSet<string>(data.SecondArchByName.Keys, StringComparer.OrdinalIgnoreCase);
            ReferenceBrowser.SetData(data);
            ExchangeFile.SetData(data);      // populate rich grid
            Export.SetData(data);
            IsrVsMsgSet.SetData(data);
            OtherReqCompare.SetData(data);
            RunPipeline();                   // level + fill + validate + checklist + dashboard + grid tint
            Status = "Loaded ✓  " + data.Summary;
            IsLoaded = true;
            CurrentPage = Dashboard;
        }
        catch (Exception ex)
        {
            Status = "Load failed: " + ex.Message;
            MessageBox.Show(ex.ToString(), "Load error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    private bool CanLoad() => !IsBusy && File_Exists(ExchangeFilePath) && File_Exists(MsgSetPath) && File_Exists(IsrAppliedPath);
    private static bool File_Exists(string p) => !string.IsNullOrWhiteSpace(p) && System.IO.File.Exists(p);

    partial void OnExchangeFilePathChanged(string value) => LoadCommand.NotifyCanExecuteChanged();
    partial void OnMsgSetPathChanged(string value) => LoadCommand.NotifyCanExecuteChanged();
    partial void OnIsrAppliedPathChanged(string value) => LoadCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value) => LoadCommand.NotifyCanExecuteChanged();
}
