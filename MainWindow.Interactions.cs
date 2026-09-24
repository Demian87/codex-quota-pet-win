using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace QuotaWisp;

public partial class MainWindow
{
    private void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pointerStart = PointToScreen(e.GetPosition(this)); _windowStart = new Point(Left, Top); _dragging = false;
        PetSurface.CaptureMouse(); e.Handled = true;
    }

    private void Pet_MouseMove(object sender, MouseEventArgs e)
    {
        if (!PetSurface.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed || _state.Settings.LockPosition) return;
        var now = PointToScreen(e.GetPosition(this)); var delta = now - _pointerStart;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 5) _dragging = true;
        if (_dragging) { Left = _windowStart.X + delta.X; Top = _windowStart.Y + delta.Y; }
    }

    private void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        PetSurface.ReleaseMouseCapture();
        if (_dragging) SavePosition();
        _dragging = false; e.Handled = true;
    }

    private void Pet_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    { BuildContextMenu(); PetSurface.ContextMenu.IsOpen = true; e.Handled = true; }

    private void PlayConsumptionReaction(int delta)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            WispImage.BeginAnimation(OpacityProperty, new DoubleAnimation(.82, 1, TimeSpan.FromMilliseconds(250)));
            return;
        }
        var scale = 1 + Math.Min(.18, delta / 100d + .05);
        var animation = new DoubleAnimation(1, scale, TimeSpan.FromMilliseconds(180))
        { AutoReverse = true, RepeatBehavior = new RepeatBehavior(delta >= 10 ? 2 : 1) };
        WispScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        WispScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item(L.T("refresh"), async () => await _state.RefreshAsync()));

        var appearance = new MenuItem { Header = L.T("appearance") };
        var sizes = new MenuItem { Header = L.T("size") };
        foreach (var size in Enum.GetValues<PetSize>())
        {
            var key = size switch { PetSize.Small => "small", PetSize.Large => "large", _ => "medium" };
            sizes.Items.Add(CheckItem(L.T(key), _state.Settings.PetSize == size, _ => _state.SetPetSize(size)));
        }
        appearance.Items.Add(sizes);
        var styles = new MenuItem { Header = L.T("tooltip") };
        foreach (var style in Enum.GetValues<TooltipStyle>())
            styles.Items.Add(CheckItem(L.T(style == TooltipStyle.Smooth ? "smooth" : "pixel"), _state.Settings.TooltipStyle == style, _ => _state.SetTooltipStyle(style)));
        appearance.Items.Add(styles);
        appearance.Items.Add(CheckItem(L.T("history"), _state.Settings.ShowHistory, v => _state.SetShowHistory(v)));
        appearance.Items.Add(Item(L.T("clearhistory"), _state.ClearHistory));
        menu.Items.Add(appearance);

        var behavior = new MenuItem { Header = L.T("behavior") };
        behavior.Items.Add(Item(_state.Settings.PetVisible ? L.T("hide") : L.T("show"), () => _state.SetPetVisible(!_state.Settings.PetVisible)));
        behavior.Items.Add(CheckItem(L.T("lock"), _state.Settings.LockPosition, v => _state.SetLockPosition(v)));
        behavior.Items.Add(CheckItem(L.T("clickthrough"), _state.Settings.ClickThrough, v => _state.SetClickThrough(v)));
        behavior.Items.Add(CheckItem(L.T("codexactive"), _state.Settings.ShowOnlyWhenCodexActive, v => _state.SetShowOnlyWhenCodexActive(v)));
        behavior.Items.Add(CheckItem(L.T("fullscreen"), _state.Settings.HideInFullscreen, v => _state.SetHideInFullscreen(v)));
        behavior.Items.Add(CheckItem(L.T("autostart"), _state.Settings.LaunchAtLogin, v => _state.SetAutoStart(v)));
        menu.Items.Add(behavior);
        PetSurface.ContextMenu = menu;
    }

    private static MenuItem Item(string title, Action action)
    { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); return item; }
    private static MenuItem CheckItem(string title, bool value, Action<bool> action)
    { var item = new MenuItem { Header = title, IsCheckable = true, IsChecked = value }; item.Click += (_, _) => action(item.IsChecked); return item; }
}
