namespace QuotaWisp;

public enum HistoryBoundary { Baseline, Continuous, Reset, Gap }

public sealed record QuotaHistoryPoint(
    DateTimeOffset ObservedAt,
    int RemainingPercent,
    HistoryBoundary BoundaryBefore);

public sealed record QuotaHistoryPresentation(
    IReadOnlyList<QuotaHistoryPoint> Points,
    DateTimeOffset RangeStart,
    DateTimeOffset RangeEnd,
    int DomainMinimum,
    int DomainMaximum,
    string Summary,
    bool ContainsReset,
    bool ContainsGap)
{
    private static readonly TimeSpan Range = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaximumComparableGap = TimeSpan.FromMinutes(90);
    private const int MaximumRenderPoints = 240;

    public static QuotaHistoryPresentation Create(IEnumerable<HistorySample> source, DateTimeOffset now)
    {
        var rangeStart = now - Range;
        var samples = source
            .Where(sample => sample.ObservedAt >= rangeStart && sample.ObservedAt <= now)
            .OrderBy(sample => sample.ObservedAt)
            .ToArray();

        var allPoints = new List<QuotaHistoryPoint>(samples.Length);
        for (var index = 0; index < samples.Length; index++)
        {
            var boundary = index == 0 ? HistoryBoundary.Baseline : Classify(samples[index - 1], samples[index]);
            allPoints.Add(new QuotaHistoryPoint(
                samples[index].ObservedAt,
                Math.Clamp(samples[index].RemainingPercent, 0, 100),
                boundary));
        }

        var summarySegment = LatestComparableSegment(allPoints);
        var summary = SummaryFor(summarySegment);
        var (minimum, maximum) = DomainFor(allPoints);
        var points = Decimate(allPoints);
        return new QuotaHistoryPresentation(
            points,
            rangeStart,
            now,
            minimum,
            maximum,
            summary,
            allPoints.Any(point => point.BoundaryBefore == HistoryBoundary.Reset),
            allPoints.Any(point => point.BoundaryBefore == HistoryBoundary.Gap));
    }

    private static HistoryBoundary Classify(HistorySample previous, HistorySample current)
    {
        var elapsed = current.ObservedAt - previous.ObservedAt;
        if (current.Gap || elapsed <= TimeSpan.Zero || elapsed > MaximumComparableGap)
            return HistoryBoundary.Gap;

        if (previous.ResetsAt != current.ResetsAt)
        {
            if (previous.ResetsAt is { } previousReset && current.ResetsAt is { } currentReset)
            {
                var previousResetTime = DateTimeOffset.FromUnixTimeSeconds(previousReset);
                var currentResetTime = DateTimeOffset.FromUnixTimeSeconds(currentReset);
                if (previousResetTime > previous.ObservedAt && previousResetTime <= current.ObservedAt &&
                    currentResetTime > current.ObservedAt)
                    return HistoryBoundary.Reset;
            }
            return HistoryBoundary.Gap;
        }

        return current.RemainingPercent <= previous.RemainingPercent
            ? HistoryBoundary.Continuous
            : HistoryBoundary.Gap;
    }

    private static IReadOnlyList<QuotaHistoryPoint> LatestComparableSegment(IReadOnlyList<QuotaHistoryPoint> points)
    {
        var segments = new List<List<QuotaHistoryPoint>>();
        List<QuotaHistoryPoint>? current = null;
        foreach (var point in points)
        {
            if (current is null || point.BoundaryBefore != HistoryBoundary.Continuous)
            {
                current = [point];
                segments.Add(current);
            }
            else current.Add(point);
        }

        var latest = segments.LastOrDefault(segment => segment.Count >= 2);
        return latest ?? (IReadOnlyList<QuotaHistoryPoint>)(segments.LastOrDefault() ?? []);
    }

    private static string SummaryFor(IReadOnlyList<QuotaHistoryPoint> segment)
    {
        if (segment.Count < 2) return L.T("historyempty");
        var start = segment[0];
        var end = segment[^1];
        var duration = end.ObservedAt - start.ObservedAt;
        var durationText = duration.TotalHours >= 1
            ? $"{Math.Max(1, (int)duration.TotalHours)}{(L.IsRussian ? "ч" : "h")}" 
            : $"{Math.Max(1, (int)duration.TotalMinutes)}{(L.IsRussian ? "м" : "m")}";
        return start.RemainingPercent == end.RemainingPercent
            ? $"{end.RemainingPercent}% · {durationText}"
            : $"{start.RemainingPercent}% → {end.RemainingPercent}% · {durationText}";
    }

    private static (int Minimum, int Maximum) DomainFor(IReadOnlyList<QuotaHistoryPoint> points)
    {
        if (points.Count == 0) return (0, 100);
        var rawMinimum = points.Min(point => point.RemainingPercent);
        var rawMaximum = points.Max(point => point.RemainingPercent);
        var lower = Math.Max(0, rawMinimum - 2) / 5 * 5;
        var upper = Math.Min(100, (rawMaximum + 6) / 5 * 5);
        while (upper - lower < 10)
        {
            if (lower > 0) lower -= 5;
            else if (upper < 100) upper += 5;
            else break;
        }
        return (lower, upper);
    }

    private static IReadOnlyList<QuotaHistoryPoint> Decimate(IReadOnlyList<QuotaHistoryPoint> points)
    {
        if (points.Count <= MaximumRenderPoints) return points;
        var selected = new SortedSet<int> { 0, points.Count - 1 };
        for (var index = 1; index < points.Count; index++)
            if (points[index].BoundaryBefore != HistoryBoundary.Continuous)
            {
                selected.Add(index - 1);
                selected.Add(index);
            }

        var step = Math.Max(1, (int)Math.Ceiling(points.Count / (double)MaximumRenderPoints));
        for (var index = 0; index < points.Count && selected.Count < MaximumRenderPoints; index += step)
            selected.Add(index);
        return selected.Take(MaximumRenderPoints).Select(index => points[index]).ToArray();
    }
}
