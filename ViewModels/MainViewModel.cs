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

        Validation.OnRerun = () => _ = RunPipelineAsync(false);
        Checklist.OnRerun = () => _ = RunPipelineAsync(false);
        Checklist.OnNavigate = NavigateToCheck;

        // The Exchange File page now owns level/fill re-run (merged from the old Level + Property Fill pages).
        ExchangeFile.OnRerun = () => _ = RunPipelineAsync(true);
        ExchangeFile.OnLoadSecondArch = () => _ = LoadSecondArchitecture();
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
    [ObservableProperty] private string _activePage = "Dashboard";
    [ObservableProperty] private string _exchangeFilePath = "";
    [ObservableProperty] private string _msgSetPath = "";
    [ObservableProperty] private string _isrAppliedPath = "";

    public string[] Architectures { get; } = { "C1A", "C1A-HS", "C1A-HS evo", "N FACE" };
    [ObservableProperty] private string _selectedArchitecture = "C1A-HS";
    [ObservableProperty] private string _status = "Select the Message List (.xlsx) and Exchange File (.xlsm), then Load.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isLoaded;

    [RelayCommand] private void NavDashboard() { CurrentPage = Dashboard; ActivePage = "Dashboard"; }
    [RelayCommand] private void NavReference() { CurrentPage = ReferenceBrowser; ActivePage = "Reference"; }
    [RelayCommand] private void NavIsrVsMsgSet() { if (IsLoaded) { CurrentPage = IsrVsMsgSet; ActivePage = "IsrVsMsgSet"; } }
    [RelayCommand] private void NavOtherReq() { if (IsLoaded) { CurrentPage = OtherReqCompare; ActivePage = "OtherReq"; } }

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

    [RelayCommand] private void NavExchange() { if (IsLoaded) { CurrentPage = ExchangeFile; ActivePage = "Exchange"; } }
    [RelayCommand] private void NavValidation() { if (IsLoaded) { CurrentPage = Validation; ActivePage = "Validation"; } }
    [RelayCommand] private void NavChecklist() { if (IsLoaded) { CurrentPage = Checklist; ActivePage = "Checklist"; } }
    [RelayCommand] private void NavExport() { if (IsLoaded) { CurrentPage = Export; ActivePage = "Export"; } }

    /// <summary>Assign levels → fill properties → validate → refresh every dependent page.</summary>
    /// <summary>
    /// Re-run the validation pipeline with the heavy compute (assign / fill / validate / detail-build)
    /// on a background thread; the awaited continuation resumes on the UI thread to push results to the
    /// bound collections. <paramref name="assignAndFill"/> = full pipeline (load / Re-run), else validate only.
    /// </summary>
    private async Task RunPipelineAsync(bool assignAndFill)
    {
        if (_data is null || IsBusy) return;
        var data = _data;
        IsBusy = true;
        Status = assignAndFill ? "Running level + fill + validation…" : "Validating…";
        try
        {
            var summaries = await Task.Run(() =>
            {
                if (assignAndFill)
                {
                    _lsvc.Assign(data.Demands, data, _secondArch);
                    _fillSvc.Fill(data.Demands, data);
                }
                var s = _vsvc.Validate(data.Demands, data);
                foreach (var d in data.Demands) _detailSvc.Build(d, data);   // side-by-side ISR vs AT detail
                return s;
            });

            // Back on the UI thread: push to the bound pages.
            Validation.SetData(data);
            Checklist.SetSummaries(summaries);
            ExchangeFile.SetData(data);          // rebuild so row severity tints refresh
            ExchangeFile.UpdatePipelineSummary(data);
            IsrVsMsgSet.SetData(data);
            OtherReqCompare.SetData(data);
            Export.SetData(data);                // refresh ready/blocked gate counts
            Dashboard.Update(data, summaries);
            Status = "Validation complete  ✓  " + data.Summary;
        }
        catch (Exception ex)
        {
            Status = "Validation failed: " + ex.Message;
            MessageBox.Show(ex.ToString(), "Validation error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    /// <summary>Checklist → Validation: filter to the clicked check's rule + worst severity, then switch page.</summary>
    private void NavigateToCheck(CheckSummary check)
    {
        if (!IsLoaded) return;
        var severity = check.Errored > 0 ? "Error" : check.Warned > 0 ? "Warning" : "All";
        Validation.FocusOn(check.Rule, severity);
        CurrentPage = Validation;
        ActivePage = "Validation";
    }

    /// <summary>Pick a 2nd-architecture Message List (resolves Level 2.1 vs 3), then re-run the pipeline.</summary>
    private async Task LoadSecondArchitecture()
    {
        if (_data is null) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Message List (*.xlsx)|*.xlsx",
            Title = "Select 2nd architecture Message List (for Level 2.1)"
        };
        if (dlg.ShowDialog() != true) return;
        IsBusy = true;
        Status = "Loading 2nd architecture…";
        try
        {
            var defs = await Task.Run(() => _loader.LoadSignalDefsByName(dlg.FileName));
            _data.SecondArchByName = defs;
            _secondArch = new HashSet<string>(defs.Keys, StringComparer.OrdinalIgnoreCase);
            ExchangeFile.SecondArchName = System.IO.Path.GetFileName(dlg.FileName) + $"  ({defs.Count:N0} signals)";
        }
        finally { IsBusy = false; }
        await RunPipelineAsync(true);
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
        IProgress<string> progress = new Progress<string>(s => Status = s);
        try
        {
            // Load + the whole compute pipeline run in a single background pass; UI updates happen after.
            var (data, summaries) = await Task.Run(() =>
            {
                var d = _loader.LoadV2(ExchangeFilePath, MsgSetPath, IsrAppliedPath, progress);
                var sa = d.SecondArchByName is null ? null
                    : new HashSet<string>(d.SecondArchByName.Keys, StringComparer.OrdinalIgnoreCase);
                progress.Report("Assigning levels + filling properties…");
                _lsvc.Assign(d.Demands, d, sa);
                _fillSvc.Fill(d.Demands, d);
                progress.Report("Validating…");
                var s = _vsvc.Validate(d.Demands, d);
                foreach (var dem in d.Demands) _detailSvc.Build(dem, d);
                return (d, s);
            });

            _data = data;
            _secondArch = data.SecondArchByName is null ? null
                : new HashSet<string>(data.SecondArchByName.Keys, StringComparer.OrdinalIgnoreCase);

            ReferenceBrowser.SetData(data);
            ExchangeFile.SetData(data);          // populate rich grid
            ExchangeFile.UpdatePipelineSummary(data);
            Export.SetData(data);
            Validation.SetData(data);
            Checklist.SetSummaries(summaries);
            IsrVsMsgSet.SetData(data);
            OtherReqCompare.SetData(data);
            Dashboard.Update(data, summaries);

            Status = "Loaded ✓  " + data.Summary;
            IsLoaded = true;
            CurrentPage = Dashboard;
            ActivePage = "Dashboard";
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
