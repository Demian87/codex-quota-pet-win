using Forms = System.Windows.Forms;

namespace QuotaWisp;

public sealed class TrayService : IDisposable
{
    private readonly AppState _state;
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon? _ownedIcon;
    private bool _menuOpen;
    private bool _menuRefreshPending;
    private bool _disposed;
    private bool _checkingForUpdates;

    public TrayService(AppState state, MainWindow window)
    {
        _state = state; _window = window;
        _ownedIcon = Environment.ProcessPath is { } path ? System.Drawing.Icon.ExtractAssociatedIcon(path) : null;
        _icon = new Forms.NotifyIcon
        {
            Text = "Quota Wisp", Icon = _ownedIcon ?? System.Drawing.SystemIcons.Application, Visible = true
        };
        _icon.DoubleClick += (_, _) => _window.BringPetBack();
        _window.SetMenuProvider(BuildMenuEntries);
        RebuildMenu();
    }

    public void RequestMenuRefresh()
    {
        if (_disposed) return;
        _window.RefreshContextMenu();
        if (_menuOpen)
        {
            _menuRefreshPending = true;
            return;
        }
        RebuildMenu();
    }

    private void RebuildMenu()
    {
        if (_disposed) return;
        _menuRefreshPending = false;
        var menu = new Forms.ContextMenuStrip();
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (!_menuRefreshPending || _disposed) return;
            _menuRefreshPending = false;
            System.Windows.Application.Current.Dispatcher.BeginInvoke(RebuildMenu);
        };
        foreach (var entry in BuildMenuEntries()) menu.Items.Add(RenderEntry(entry));
        var old = _icon.ContextMenuStrip; _icon.ContextMenuStrip = menu; old?.Dispose();
        var primary = _state.Quota?.Primary;
        _icon.Text = primary is null ? "Quota Wisp" : $"Quota Wisp — {primary.RemainingPercent}%";
    }

    public IReadOnlyList<PetMenuEntry> BuildMenuEntries()
    {
        var menu = new List<PetMenuEntry>();
        var primary = _state.Quota?.Primary;
        Add(menu, primary is null ? $"{L.T("available")}: —" : $"{L.T("available")}: {primary.RemainingPercent}%", null, false);
        if (_state.Quota?.Secondary is { } secondary)
            Add(menu, $"{L.T("weekly")}: {secondary.RemainingPercent}%", null, false);
        Add(menu, $"{L.T("resetcredits")}: {ResetCreditsFormatter.Format(_state.ResetCreditsAvailableCount)}", null, false);
        if (_state.Connection != ConnectionStatus.Connected)
            Add(menu, _state.LastError ?? L.T("reconnecting"), null, false);
        menu.Add(PetMenuEntry.Separator());
        Add(menu, L.T("refresh"), async () => await _state.RefreshAsync());

        var appearance = Submenu(L.T("appearance"));
        var size = Submenu(L.T("size"));
        AddCheck(size, L.T("small"), _state.Settings.PetSize == PetSize.Small, () => _state.SetPetSize(PetSize.Small));
        AddCheck(size, L.T("medium"), _state.Settings.PetSize == PetSize.Medium, () => _state.SetPetSize(PetSize.Medium));
        AddCheck(size, L.T("large"), _state.Settings.PetSize == PetSize.Large, () => _state.SetPetSize(PetSize.Large));
        appearance.Children!.Add(size);

        var tooltip = Submenu(L.T("tooltip"));
        AddCheck(tooltip, L.T("smooth"), _state.Settings.TooltipStyle == TooltipStyle.Smooth, () => _state.SetTooltipStyle(TooltipStyle.Smooth));
        AddCheck(tooltip, L.T("pixel"), _state.Settings.TooltipStyle == TooltipStyle.Pixel, () => _state.SetTooltipStyle(TooltipStyle.Pixel));
        appearance.Children!.Add(tooltip);
        AddCheck(appearance, L.T("history"), _state.Settings.ShowHistory, () => _state.SetShowHistory(!_state.Settings.ShowHistory));
        Add(appearance, L.T("clearhistory"), _state.ClearHistory);

        var language = Submenu(L.T("language"));
        AddCheck(language, L.T("auto"), _state.Settings.Language == UiLanguage.Auto, () => _state.SetLanguage(UiLanguage.Auto));
        AddCheck(language, L.T("english"), _state.Settings.Language == UiLanguage.English, () => _state.SetLanguage(UiLanguage.English));
        AddCheck(language, L.T("russian"), _state.Settings.Language == UiLanguage.Russian, () => _state.SetLanguage(UiLanguage.Russian));
        appearance.Children!.Add(language);
        menu.Add(appearance);

        var behavior = Submenu(L.T("behavior"));
        Add(behavior, _state.Settings.PetVisible ? L.T("hide") : L.T("show"), () =>
        {
            _state.SetPetVisible(!_state.Settings.PetVisible);
            if (_state.Settings.PetVisible) _window.BringPetBack();
        });
        AddCheck(behavior, L.T("lock"), _state.Settings.LockPosition, () => _state.SetLockPosition(!_state.Settings.LockPosition));
        AddCheck(behavior, L.T("clickthrough"), _state.Settings.ClickThrough, () => _state.SetClickThrough(!_state.Settings.ClickThrough));
        AddCheck(behavior, L.T("codexactive"), _state.Settings.ShowOnlyWhenCodexActive, () => _state.SetShowOnlyWhenCodexActive(!_state.Settings.ShowOnlyWhenCodexActive));
        AddCheck(behavior, L.T("fullscreen"), _state.Settings.HideInFullscreen, () => _state.SetHideInFullscreen(!_state.Settings.HideInFullscreen));
        AddCheck(behavior, L.T("autostart"), _state.Settings.LaunchAtLogin, () => _state.SetAutoStart(!_state.Settings.LaunchAtLogin));
        Add(behavior, L.T("codexpath"), SelectCodexPath);
        menu.Add(behavior);

        Add(menu, _checkingForUpdates ? L.T("updatechecking") : L.T("checkupdates"), CheckForUpdates, !_checkingForUpdates);
        menu.Add(PetMenuEntry.Separator());
        Add(menu, L.T("quit"), () => System.Windows.Application.Current.Shutdown());

        return menu;
    }

    private void SelectCodexPath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.T("codexpath"), Filter = "Codex executable|codex.exe;codex.cmd;codex.ps1|All files|*.*"
        };
        if (dialog.ShowDialog() == true) _state.SetCodexPath(dialog.FileName);
    }

    private async void CheckForUpdates()
    {
        if (_checkingForUpdates || _disposed) return;
        _checkingForUpdates = true;
        RequestMenuRefresh();
        UpdatePackage? package = null;
        try
        {
            using var manager = new UpdateManager();
            var release = await manager.CheckAsync(CancellationToken.None);
            if (release is null)
            {
                System.Windows.MessageBox.Show(string.Format(L.T("updatecurrent"), UpdateManager.CurrentVersion),
                    L.T("updatetitle"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            if (System.Windows.MessageBox.Show(string.Format(L.T("updateavailable"), release.Version, UpdateManager.CurrentVersion),
                    L.T("updatetitle"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
                return;

            package = await manager.DownloadAndVerifyAsync(release, CancellationToken.None);
            if (System.Windows.MessageBox.Show(L.T("updateready"), L.T("updatetitle"),
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            {
                package.Delete();
                return;
            }

            await _state.FlushHistoryForUpdateAsync(TimeSpan.FromSeconds(10));
            manager.StartInstaller(package);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            package?.Delete();
            System.Windows.MessageBox.Show(string.Format(L.T("updateerror"), error.Message), L.T("updatetitle"),
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            _checkingForUpdates = false;
            RequestMenuRefresh();
        }
    }

    public void ShowNotification(string message)
    { _icon.BalloonTipTitle = L.T("app"); _icon.BalloonTipText = message; _icon.ShowBalloonTip(5000); }

    private static PetMenuEntry Submenu(string title) => new(title, Children: []);
    private static void Add(List<PetMenuEntry> menu, string text, Action? action, bool enabled = true)
        => menu.Add(new(text, action, enabled));
    private static void Add(PetMenuEntry menu, string text, Action? action, bool enabled = true)
        => Add(menu.Children!, text, action, enabled);
    private static void AddCheck(PetMenuEntry menu, string text, bool check, Action action)
        => menu.Children!.Add(new(text, action, Checked: check));

    internal static Forms.ToolStripItem RenderEntry(PetMenuEntry entry)
    {
        if (entry.IsSeparator) return new Forms.ToolStripSeparator();
        var item = new Forms.ToolStripMenuItem(entry.Text) { Enabled = entry.Enabled, Checked = entry.Checked == true };
        if (entry.Children is { } children)
            foreach (var child in children) item.DropDownItems.Add(RenderEntry(child));
        if (entry.Action is { } action) item.Click += (_, _) => action();
        return item;
    }

    public void Dispose()
    {
        _disposed = true;
        _menuRefreshPending = false;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _ownedIcon?.Dispose();
    }
}
