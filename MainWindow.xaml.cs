using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace QuotaWisp;

public partial class MainWindow : Window
{
    private readonly AppState _state;
    private readonly DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _visibilityTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Random _random = new();
    private Point _pointerStart, _windowStart;
    private bool _dragging, _fullscreenSuppressed, _codexActive = true;
    private bool? _nativeClickThrough;
    private double _phase;
    private PetSize? _appliedSize;
    private int _quotaImageBucket = 100;
    private static readonly Dictionary<int, BitmapImage> QuotaImages = [];

    public MainWindow(AppState state)
    {
        _state = state;
        InitializeComponent();
        _state.Changed += (_, _) => Dispatcher.BeginInvoke(UpdateFromState);
        _state.QuotaConsumed += (_, delta) => Dispatcher.BeginInvoke(() =>
        {
            RefreshVisibilityConditions();
            if (ShouldPresentPet() && IsVisible) PlayConsumptionReaction(delta);
        });
        Loaded += OnLoaded;
        LocationChanged += (_, _) => { if (!_dragging) { _positionTimer.Stop(); _positionTimer.Start(); } };
        _positionTimer.Tick += (_, _) => { _positionTimer.Stop(); SavePosition(); };
        _animationTimer.Tick += Animate;
        _visibilityTimer.Tick += (_, _) => CheckVisibilityConditions();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestorePosition(); ApplySize(); RefreshVisibilityConditions(); UpdateFromState(); ApplyClickThrough();
        _animationTimer.Start(); _visibilityTimer.Start(); BuildContextMenu();
    }

    private void UpdateFromState()
    {
        ApplySize(); ApplyClickThrough(); RefreshVisibilityConditions(); ApplyVisibility(); UpdateTooltip(); UpdateQuotaImage();
        AutomationProperties.SetName(WispImage, AccessibleSummary()); BuildContextMenu();
    }

    private void Animate(object? sender, EventArgs e)
    {
        UpdatePointerTransparency();
        if (!SystemParameters.ClientAreaAnimation)
        { OrbitRotate.Angle = 0; WispBob.Y = 0; WispScale.ScaleX = WispScale.ScaleY = 1; return; }
        var remaining = _state.Quota?.Primary?.RemainingPercent ?? 50;
        var speed = (0.15 + remaining / 100d * 0.85) * (_state.SpeedMode == SpeedMode.Turbo ? 1.5 : 1);
        _phase += speed;
        OrbitRotate.Angle = _phase; WispBob.Y = Math.Sin(_phase * Math.PI / 90) * 3;
        var pulse = _state.SpeedMode == SpeedMode.Turbo ? 1 + Math.Sin(_phase * Math.PI / 45) * .025 : 1;
        WispScale.ScaleX = WispScale.ScaleY = pulse;
    }

    private void ApplySize()
    {
        var sizeChanged = _appliedSize != _state.Settings.PetSize;
        var layout = PetLayout.For(_state.Settings.PetSize);
        Width = layout.WindowWidth; Height = layout.WindowHeight;
        PetSurface.Width = PetSurface.Height = layout.OrbitSize;
        OrbitCanvas.Width = OrbitCanvas.Height = layout.OrbitSize;
        WispImage.Width = WispImage.Height = layout.MoonSize;
        SatelliteLeftScale.ScaleX = SatelliteLeftScale.ScaleY = layout.SatelliteScale;
        SatelliteRightScale.ScaleX = SatelliteRightScale.ScaleY = layout.SatelliteScale;
        SatelliteTopScale.ScaleX = SatelliteTopScale.ScaleY = layout.SatelliteScale;
        PlaceSatellite(SatelliteLeft, layout, 180);
        PlaceSatellite(SatelliteRight, layout, -14);
        PlaceSatellite(SatelliteTop, layout, -86);
        _appliedSize = _state.Settings.PetSize;
        if (IsLoaded && sizeChanged) ClampToScreens();
    }

    private bool IsVisiblePetPoint(Point point)
    {
        var layout = PetLayout.For(_state.Settings.PetSize);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var satellites = new[] { SatelliteLeft, SatelliteRight, SatelliteTop }.Select(satellite =>
            satellite.TransformToAncestor(this).TransformBounds(new Rect(0, 0, satellite.ActualWidth, satellite.ActualHeight)));
        return PetInputRegion.Contains(point, center, layout.MoonSize / 2, satellites);
    }

    private static void PlaceSatellite(Canvas satellite, PetLayout layout, double angle)
    {
        var radians = angle * Math.PI / 180;
        var center = layout.OrbitSize / 2;
        Canvas.SetLeft(satellite, center + layout.OrbitRadius * Math.Cos(radians) - satellite.Width / 2);
        Canvas.SetTop(satellite, center + layout.OrbitRadius * Math.Sin(radians) - satellite.Height / 2);
    }

    private void UpdateQuotaImage()
    {
        var remaining = _state.Quota?.Primary?.RemainingPercent;
        if (remaining is null) return;

        var bucket = QuotaMoon.BucketFor(remaining.Value);
        if (bucket == _quotaImageBucket) return;

        if (!QuotaImages.TryGetValue(bucket, out var image))
        {
            image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(QuotaMoon.AssetUri(bucket), UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            QuotaImages[bucket] = image;
        }

        WispImage.Source = image;
        _quotaImageBucket = bucket;
    }

    private void UpdateTooltip()
    {
        var primary = _state.Quota?.Primary;
        AvailableLabel.Text = L.T("available"); PrimaryPercent.Text = primary is null ? "—" : $"{primary.RemainingPercent}%";
        PrimaryProgress.Value = primary?.RemainingPercent ?? 0;
        ModeText.Text = L.T(_state.SpeedMode == SpeedMode.Turbo ? "turbo" : "standard");
        PrimaryReset.Text = $"{L.T("reset")}: {FormatReset(primary?.ResetTime)}";
        var secondary = _state.Quota?.Secondary;
        SecondaryLabel.Text = L.T("weekly"); SecondaryPercent.Text = secondary is null ? "—" : $"{secondary.RemainingPercent}%";
        SecondaryReset.Text = secondary is null ? "" : $"{L.T("reset")}: {FormatReset(secondary.ResetTime)}";
        SecondaryPanel.Visibility = SecondarySeparator.Visibility = SecondaryReset.Visibility = secondary is null ? Visibility.Collapsed : Visibility.Visible;
        var resetCredits = ResetCreditsFormatter.Format(_state.ResetCreditsAvailableCount);
        ResetCreditsLabel.Text = L.T("resetcredits");
        ResetCreditsCount.Text = resetCredits ?? "";
        ResetCreditsPanel.Visibility = resetCredits is null ? Visibility.Collapsed : Visibility.Visible;
        HistoryPanel.Visibility = _state.Settings.ShowHistory ? Visibility.Visible : Visibility.Collapsed;
        HistoryLabel.Text = L.T("historytitle");
        var history = _state.History.Presentation();
        HistorySummary.Text = history.Summary;
        HistoryChart.Presentation = history;
        ConnectionText.Text = _state.Connection == ConnectionStatus.Connected ? "" : ConnectionLabel();
        ConnectionText.Visibility = string.IsNullOrEmpty(ConnectionText.Text) ? Visibility.Collapsed : Visibility.Visible;
        var pixel = _state.Settings.TooltipStyle == TooltipStyle.Pixel;
        TooltipBorder.CornerRadius = pixel ? new CornerRadius(0) : new CornerRadius(12);
        TooltipBorder.BorderThickness = pixel ? new Thickness(2) : new Thickness(1);
        TooltipBorder.Background = new SolidColorBrush(pixel ? Color.FromArgb(252, 4, 13, 23) : Color.FromArgb(250, 7, 17, 30));
        System.Windows.Documents.TextElement.SetFontFamily(TooltipBorder, new System.Windows.Media.FontFamily(pixel ? "Consolas" : "Segoe UI"));
    }

    private string FormatReset(DateTimeOffset? reset) => reset is null ? L.T("unknown") : $"{reset.Value:g} ({HumanDuration(reset.Value - DateTimeOffset.Now)})";
    private static string HumanDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return L.IsRussian ? "сейчас" : "now";
        return L.IsRussian ? (duration.TotalDays >= 1 ? $"через {Math.Ceiling(duration.TotalDays)} дн." : $"через {Math.Ceiling(duration.TotalHours)} ч.")
            : (duration.TotalDays >= 1 ? $"in {Math.Ceiling(duration.TotalDays)}d" : $"in {Math.Ceiling(duration.TotalHours)}h");
    }

    private string ConnectionLabel() => (_state.Connection switch
    { ConnectionStatus.Connecting => L.T("connecting"), ConnectionStatus.Reconnecting => L.T("reconnecting"), _ => L.T("disconnected") })
        + (string.IsNullOrWhiteSpace(_state.LastError) ? "" : $"\n{_state.LastError}");
    private string AccessibleSummary() => $"{L.T("app")}. {PrimaryPercent.Text} {L.T("available")}. {ModeText.Text}. {ConnectionLabel()}";
    private void QuotaToolTip_Opened(object sender, RoutedEventArgs e) { _ = _state.RefreshAsync(); UpdateTooltip(); }
    private void Pet_MouseEnter(object sender, MouseEventArgs e) => _ = _state.RefreshAsync();
    private void Pet_MouseLeave(object sender, MouseEventArgs e) { }

    private void ApplyClickThrough() { if (IsLoaded) UpdatePointerTransparency(force: true); }
    private void UpdatePointerTransparency(bool force = false)
    {
        if (!IsLoaded) return;
        var cursor = System.Windows.Forms.Cursor.Position;
        var point = PointFromScreen(new Point(cursor.X, cursor.Y));
        var enabled = _state.Settings.ClickThrough || !IsVisiblePetPoint(point);
        if (!force && _nativeClickThrough == enabled) return;
        Win32.SetClickThrough(this, enabled);
        _nativeClickThrough = enabled;
    }
    private void CheckVisibilityConditions()
    {
        RefreshVisibilityConditions();
        ApplyVisibility();
    }
    private void RefreshVisibilityConditions()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _fullscreenSuppressed = _state.Settings.HideInFullscreen && Win32.IsForegroundFullscreen(handle);
        _codexActive = !_state.Settings.ShowOnlyWhenCodexActive || Win32.IsCodexForeground(handle);
    }
    private bool ShouldPresentPet() => PetVisibilityPolicy.ShouldShow(
        _state.Settings.PetVisible, _fullscreenSuppressed, _state.Settings.ShowOnlyWhenCodexActive, _codexActive);
    private void ApplyVisibility()
    {
        if (ShouldPresentPet())
        { if (!IsVisible) Show(); Opacity = 1; }
        else if (IsVisible) Hide();
    }

    private static string ScreenKey() => string.Join("|", System.Windows.Forms.Screen.AllScreens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y)
        .Select(s => $"{s.DeviceName}:{s.Bounds.X},{s.Bounds.Y},{s.Bounds.Width},{s.Bounds.Height}"));
    private void RestorePosition()
    {
        if (_state.Settings.Positions.TryGetValue(ScreenKey(), out var saved)) { Left = saved.Left; Top = saved.Top; }
        else { Left = SystemParameters.WorkArea.Right - Width - 24; Top = SystemParameters.WorkArea.Bottom - Height - 24; }
        ClampToScreens();
    }
    private void SavePosition() { if (IsLoaded) _state.SavePosition(ScreenKey(), Left, Top); }
    private void ClampToScreens()
    {
        var area = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }
    public void BringPetBack() { _state.SetPetVisible(true); RefreshVisibilityConditions(); ApplyVisibility(); Topmost = true; }
}
