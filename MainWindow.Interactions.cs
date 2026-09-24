using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;

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
        if (_dragging) SavePosition(); else SpawnAbsorbable();
        _dragging = false; e.Handled = true;
    }

    private void Pet_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    { BuildContextMenu(); PetSurface.ContextMenu.IsOpen = true; e.Handled = true; }

    private void SpawnAbsorbable()
    {
        if (!SystemParameters.ClientAreaAnimation) { PlayConsumptionReaction(1); return; }
        var category = WeightedCategory();
        var glyphs = category switch
        {
            "nature" => new[] { "✿", "❧", "◆", "❀", "☘", "♒", "△", "◌", "❉", "♢", "☂" },
            "code" => new[] { "{ }", "</>", "#", "01", "λ", "git", "fn", "[]", "=>", "//", "AI" },
            _ => new[] { "✦", "☄", "◇", "☾", "⊙", "✧", "◉", "⋆", "◎", "◈", "✺", "☼" }
        };
        var item = new TextBlock { Text = glyphs[_random.Next(glyphs.Length)], FontSize = 24, Foreground = Brushes.White };
        EffectsCanvas.Children.Add(item);
        var startX = _random.NextDouble() * Math.Max(1, ActualWidth - 30); var startY = _random.NextDouble() * Math.Max(1, ActualHeight - 30);
        Canvas.SetLeft(item, startX); Canvas.SetTop(item, startY);
        var duration = new Duration(TimeSpan.FromMilliseconds(700));
        item.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(ActualWidth / 2, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        item.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(ActualHeight / 2, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        var fade = new DoubleAnimation(1, 0, duration); fade.Completed += (_, _) => EffectsCanvas.Children.Remove(item);
        item.BeginAnimation(OpacityProperty, fade);
    }

    private string WeightedCategory()
    {
        var weighted = _state.Settings.ObjectWeights.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)).ToArray();
        return weighted.Length == 0 ? "space" : weighted[_random.Next(weighted.Length)];
    }

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
        for (var i = 0; i < Math.Min(8, 2 + delta / 2); i++) SpawnAbsorbable();
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

        var objects = new MenuItem { Header = L.T("objects") };
        AddWeightMenu(objects, "Space / Космос", "space");
        AddWeightMenu(objects, "Nature / Природа", "nature");
        AddWeightMenu(objects, "Code / Код", "code");
        menu.Items.Add(objects);

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

    private void AddWeightMenu(MenuItem parent, string title, string category)
    {
        var child = new MenuItem { Header = title };
        var current = _state.Settings.ObjectWeights.GetValueOrDefault(category, 1);
        for (var value = 0; value <= 3; value++)
        {
            var captured = value;
            child.Items.Add(CheckItem(value.ToString(), current == value, _ => _state.SetObjectWeight(category, captured)));
        }
        parent.Items.Add(child);
    }

    private static MenuItem Item(string title, Action action)
    { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); return item; }
    private static MenuItem CheckItem(string title, bool value, Action<bool> action)
    { var item = new MenuItem { Header = title, IsCheckable = true, IsChecked = value }; item.Click += (_, _) => action(item.IsChecked); return item; }
}
