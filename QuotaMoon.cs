namespace QuotaWisp;

public static class QuotaMoon
{
    public static int BucketFor(int remainingPercent)
    {
        var clamped = Math.Clamp(remainingPercent, 0, 100);
        return Math.Max(10, (int)Math.Ceiling(clamped / 10d) * 10);
    }

    public static string AssetUri(int bucket)
    {
        var fileName = bucket >= 100 ? "quota-wisp.png" : $"quota-wisp-{bucket}.png";
        return $"pack://application:,,,/Assets/{fileName}";
    }
}
