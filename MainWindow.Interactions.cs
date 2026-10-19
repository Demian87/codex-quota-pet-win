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

    private Func<IReadOnlyList<PetMenuEntry>>? _menuProvider;

    public void SetMenuProvider(Func<IReadOnlyList<PetMenuEntry>> provider)
    {
        _menuProvider = provider;
        BuildContextMenu();
    }

    public void RefreshContextMenu() => BuildContextMenu();

    private void BuildContextMenu()
    {
        // Do not replace an open popup during quota refresh or keyboard navigation.
        if (PetSurface.ContextMenu?.IsOpen == true) return;
        var menu = new ContextMenu();
        foreach (var entry in _menuProvider?.Invoke() ?? []) menu.Items.Add(RenderMenuEntry(entry));
        menu.Closed += (_, _) => Dispatcher.BeginInvoke(BuildContextMenu);
        PetSurface.ContextMenu = menu;
    }

    internal static object RenderMenuEntry(PetMenuEntry entry)
    {
        if (entry.IsSeparator) return new Separator();
        var item = new MenuItem
        {
            Header = entry.Text, IsEnabled = entry.Enabled,
            IsCheckable = entry.Checked.HasValue, IsChecked = entry.Checked == true
        };
        if (entry.Children is { } children)
            foreach (var child in children) item.Items.Add(RenderMenuEntry(child));
        if (entry.Action is { } action) item.Click += (_, e) =>
        {
            e.Handled = true;
            // Let the popup close before opening dialogs or hiding the pet.
            item.Dispatcher.BeginInvoke(action);
        };
        return item;
    }
}
