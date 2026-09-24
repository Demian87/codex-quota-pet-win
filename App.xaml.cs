using System.Configuration;
using System.Data;
using System.Windows;

namespace QuotaWisp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private System.Threading.Mutex? _singleInstance;
    private TrayService? _tray;
    private AppState? _state;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (await UpdateBootstrapper.TryRunInstallerAsync(e.Args))
        {
            Shutdown();
            return;
        }
        UpdateBootstrapper.ScheduleCleanup(e.Args);
        _singleInstance = new System.Threading.Mutex(true, "QuotaWisp.Win11.SingleInstance", out var created);
        if (!created) { Shutdown(); return; }

        _state = new AppState();
        var petWindow = new MainWindow(_state);
        _tray = new TrayService(_state, petWindow);
        _state.TrayRefreshRequested += (_, _) => Dispatcher.BeginInvoke(_tray.RequestMenuRefresh);
        _state.LowQuotaNotification += (_, message) => Dispatcher.BeginInvoke(() => _tray.ShowNotification(message));
        petWindow.Show();
        _ = _state.StartAsync();
        if (e.Args.Contains("--update-install-failed", StringComparer.Ordinal))
            _ = Dispatcher.BeginInvoke(() => System.Windows.MessageBox.Show(L.T("updateinstallerror"), L.T("updatetitle"), MessageBoxButton.OK, MessageBoxImage.Error));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _state?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

