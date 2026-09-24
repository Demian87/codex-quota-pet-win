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

    public TrayService(AppState state, MainWindow window)
    {
        _state = state; _window = window;
        _ownedIcon = Environment.ProcessPath is { } path ? System.Drawing.Icon.ExtractAssociatedIcon(path) : null;
        _icon = new Forms.NotifyIcon
        {
            Text = "Quota Wisp", Icon = _ownedIcon ?? System.Drawing.SystemIcons.Application, Visible = true
        };
        _icon.DoubleClick += (_, _) => _window.BringPetBack();
        RebuildMenu();
    }

    public void RequestMenuRefresh()
    {
        if (_disposed) return;
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
        var primary = _state.Quota?.Primary;
        Add(menu, primary is null ? $"{L.T("available")}: —" : $"{L.T("available")}: {primary.RemainingPercent}%", null, false);
        if (_state.Quota?.Secondary is { } secondary)
            Add(menu, $"{L.T("weekly")}: {secondary.RemainingPercent}%", null, false);
        if (_state.Connection != ConnectionStatus.Connected)
            Add(menu, _state.LastError ?? L.T("reconnecting"), null, false);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add(menu, L.T("refresh"), async () => await _state.RefreshAsync());

        var appearance = Submenu(L.T("appearance"));
        var size = Submenu(L.T("size"));
        AddCheck(size, L.T("small"), _state.Settings.PetSize == PetSize.Small, () => _state.SetPetSize(PetSize.Small));
        AddCheck(size, L.T("medium"), _state.Settings.PetSize == PetSize.Medium, () => _state.SetPetSize(PetSize.Medium));
        AddCheck(size, L.T("large"), _state.Settings.PetSize == PetSize.Large, () => _state.SetPetSize(PetSize.Large));
        appearance.DropDownItems.Add(size);

        var tooltip = Submenu(L.T("tooltip"));
        AddCheck(tooltip, L.T("smooth"), _state.Settings.TooltipStyle == TooltipStyle.Smooth, () => _state.SetTooltipStyle(TooltipStyle.Smooth));
        AddCheck(tooltip, L.T("pixel"), _state.Settings.TooltipStyle == TooltipStyle.Pixel, () => _state.SetTooltipStyle(TooltipStyle.Pixel));
        appearance.DropDownItems.Add(tooltip);
        AddCheck(appearance, L.T("history"), _state.Settings.ShowHistory, () => _state.SetShowHistory(!_state.Settings.ShowHistory));
        Add(appearance, L.T("clearhistory"), _state.ClearHistory);

        var language = Submenu(L.T("language"));
        AddCheck(language, L.T("auto"), _state.Settings.Language == UiLanguage.Auto, () => _state.SetLanguage(UiLanguage.Auto));
        AddCheck(language, L.T("english"), _state.Settings.Language == UiLanguage.English, () => _state.SetLanguage(UiLanguage.English));
        AddCheck(language, L.T("russian"), _state.Settings.Language == UiLanguage.Russian, () => _state.SetLanguage(UiLanguage.Russian));
        appearance.DropDownItems.Add(language);
        menu.Items.Add(appearance);

        var objects = Submenu(L.T("objects"));
        AddWeightMenu(objects, "Space / Космос", "space"); AddWeightMenu(objects, "Nature / Природа", "nature"); AddWeightMenu(objects, "Code / Код", "code");
        menu.Items.Add(objects);

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
        menu.Items.Add(behavior);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add(menu, L.T("quit"), () => System.Windows.Application.Current.Shutdown());

        var old = _icon.ContextMenuStrip; _icon.ContextMenuStrip = menu; old?.Dispose();
        _icon.Text = primary is null ? "Quota Wisp" : $"Quota Wisp — {primary.RemainingPercent}%";
    }

    private void AddWeightMenu(Forms.ToolStripMenuItem parent, string title, string category)
    {
        var child = Submenu(title); var current = _state.Settings.ObjectWeights.GetValueOrDefault(category, 1);
        for (var value = 0; value <= 3; value++)
        { var captured = value; AddCheck(child, value.ToString(), current == value, () => _state.SetObjectWeight(category, captured)); }
        parent.DropDownItems.Add(child);
    }

    private void SelectCodexPath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.T("codexpath"), Filter = "Codex executable|codex.exe;codex.cmd;codex.ps1|All files|*.*"
        };
        if (dialog.ShowDialog() == true) _state.SetCodexPath(dialog.FileName);
    }

    public void ShowNotification(string message)
    { _icon.BalloonTipTitle = L.T("app"); _icon.BalloonTipText = message; _icon.ShowBalloonTip(5000); }

    private static Forms.ToolStripMenuItem Submenu(string title) => new(title);
    private static void Add(Forms.ContextMenuStrip menu, string text, Action? action, bool enabled = true)
    { var item = new Forms.ToolStripMenuItem(text) { Enabled = enabled }; if (action is not null) item.Click += (_, _) => action(); menu.Items.Add(item); }
    private static void Add(Forms.ToolStripMenuItem menu, string text, Action? action, bool enabled = true)
    { var item = new Forms.ToolStripMenuItem(text) { Enabled = enabled }; if (action is not null) item.Click += (_, _) => action(); menu.DropDownItems.Add(item); }
    private static void AddCheck(Forms.ContextMenuStrip menu, string text, bool check, Action action)
    { var item = new Forms.ToolStripMenuItem(text) { Checked = check }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    private static void AddCheck(Forms.ToolStripMenuItem menu, string text, bool check, Action action)
    { var item = new Forms.ToolStripMenuItem(text) { Checked = check }; item.Click += (_, _) => action(); menu.DropDownItems.Add(item); }

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
