using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace QuotaWisp;

public static class PetInputRegion
{
    public static bool Contains(Point point, Point moonCenter, double moonRadius, IEnumerable<Rect> satelliteBounds)
    {
        var dx = point.X - moonCenter.X;
        var dy = point.Y - moonCenter.Y;
        return dx * dx + dy * dy <= moonRadius * moonRadius
            || satelliteBounds.Any(bounds => bounds.Contains(point));
    }
}
