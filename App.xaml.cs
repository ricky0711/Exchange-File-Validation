using System.Windows;
using System.Windows.Media;
using ExchangeFileValidator.Services;
using ExchangeFileValidator.ViewModels;
using Wpf.Ui.Appearance;

namespace ExchangeFileValidator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Indigo/violet identity (carried from TeamTrack)
        ApplicationThemeManager.Apply(ApplicationTheme.Light);
        ApplicationAccentColorManager.Apply(
            (Color)ColorConverter.ConvertFromString("#6D5DF5")!,   // indigo-violet (positional args for WPF-UI 4.x)
            ApplicationTheme.Light);

        var loader = new ReferenceDataLoader();
        var mainVm = new MainViewModel(loader);
        var window = new MainWindow { DataContext = mainVm };
        window.Show();
    }
}
