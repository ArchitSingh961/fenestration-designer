using System.Windows;
using Fenestration.Designer.ViewModels;

namespace Fenestration.App;

/// <summary>
/// Application entry point. Composes the view model graph and shows the main window.
/// (StartupUri is intentionally not used so the composition root stays in one place.)
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainViewModel = new MainViewModel();
        var window = new MainWindow { DataContext = mainViewModel };
        window.Show();
    }
}
