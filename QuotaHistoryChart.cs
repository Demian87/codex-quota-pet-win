using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace QuotaWisp;

public sealed class QuotaHistoryChart : FrameworkElement
{
    private static readonly Brush GridBrush = FrozenBrush(Color.FromArgb(36, 91, 142, 158));
    private static readonly Brush LabelBrush = FrozenBrush(Color.FromRgb(103, 137, 151));
    private static readonly Brush GapBrush = FrozenBrush(Color.FromArgb(26, 185, 204, 211));
    private static readonly Pen LinePen = FrozenPen(Color.FromRgb(74, 229, 244), 1.7);
    private static readonly Pen GapPen = FrozenPen(Color.FromArgb(120, 185, 204, 211), 1, DashStyles.Dash);
    private static readonly Pen ResetPen = FrozenPen(Color.FromRgb(186, 92, 244), 1.5);
    private static readonly Typeface ChartTypeface = new("Consolas");
    private QuotaHistoryPresentation? _presentation;

    public QuotaHistoryPresentation? Presentation
    {
        get => _presentation;
        set { _presentation = value; InvalidateVisual(); }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var presentation = _presentation;
        if (presentation is null || ActualWidth < 90 || ActualHeight < 40) return;

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var plot = new Rect(31, 4, Math.Max(1, ActualWidth - 35), Math.Max(1, ActualHeight - 18));
        var gridPen = new Pen(GridBrush, 1);
        foreach (var fraction in new[] { .25, .75 })
        {
            var y = plot.Top + plot.Height * fraction;
            drawingContext.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        DrawText(drawingContext, $"{presentation.DomainMaximum}%", new Point(0, 0), LabelBrush, pixelsPerDip);
        DrawText(drawingContext, $"{presentation.DomainMinimum}%", new Point(0, plot.Bottom - 8), LabelBrush, pixelsPerDip);
        DrawText(drawingContext, L.T("history24h"), new Point(plot.Left, plot.Bottom + 2), LabelBrush, pixelsPerDip);
        var nowText = Formatted(L.T("historynow"), LabelBrush, pixelsPerDip);
        drawingContext.DrawText(nowText, new Point(plot.Right - nowText.Width, plot.Bottom + 2));

        var points = presentation.Points;
        if (points.Count < 2) return;
        Point Position(QuotaHistoryPoint point)
        {
            var xFraction = Math.Clamp(
                (point.ObservedAt - presentation.RangeStart).TotalSeconds /
                Math.Max(1, (presentation.RangeEnd - presentation.RangeStart).TotalSeconds), 0, 1);
            var yFraction = (point.RemainingPercent - presentation.DomainMinimum) /
                (double)Math.Max(1, presentation.DomainMaximum - presentation.DomainMinimum);
            return new Point(plot.Left + plot.Width * xFraction, plot.Bottom - plot.Height * Math.Clamp(yFraction, 0, 1));
        }

        for (var index = 1; index < points.Count; index++)
        {
            var start = Position(points[index - 1]);
            var end = Position(points[index]);
            switch (points[index].BoundaryBefore)
            {
                case HistoryBoundary.Continuous:
                    drawingContext.DrawLine(LinePen, start, end);
                    break;
                case HistoryBoundary.Gap:
                    var gap = new Rect(Math.Min(start.X, end.X), plot.Top, Math.Max(1, Math.Abs(end.X - start.X)), plot.Height);
                    drawingContext.DrawRectangle(GapBrush, GapPen, gap);
                    break;
                case HistoryBoundary.Reset:
                    var center = new Point((start.X + end.X) / 2, plot.Top + plot.Height / 2);
                    var diamond = new StreamGeometry();
                    using (var context = diamond.Open())
                    {
                        context.BeginFigure(new Point(center.X, center.Y - 4), true, true);
                        context.LineTo(new Point(center.X + 4, center.Y), true, false);
                        context.LineTo(new Point(center.X, center.Y + 4), true, false);
                        context.LineTo(new Point(center.X - 4, center.Y), true, false);
                    }
                    diamond.Freeze();
                    drawingContext.DrawGeometry(Brushes.Black, ResetPen, diamond);
                    break;
            }
        }

        var first = Position(points[0]);
        drawingContext.DrawEllipse(LinePen.Brush, null, first, 2.5, 2.5);
        var last = Position(points[^1]);
        drawingContext.DrawEllipse(Brushes.Black, LinePen, last, 4, 4);
        drawingContext.DrawEllipse(LinePen.Brush, null, last, 1.5, 1.5);
    }

    private static void DrawText(DrawingContext context, string text, Point origin, Brush brush, double pixelsPerDip) =>
        context.DrawText(Formatted(text, brush, pixelsPerDip), origin);

    private static FormattedText Formatted(string text, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight, ChartTypeface, 8, brush, pixelsPerDip);

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }

    private static Pen FrozenPen(Color color, double thickness, DashStyle? dash = null)
    {
        var pen = new Pen(FrozenBrush(color), thickness) { DashStyle = dash ?? DashStyles.Solid };
        pen.Freeze(); return pen;
    }
}
