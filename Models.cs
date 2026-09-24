namespace QuotaWisp;

public sealed record QuotaWindow(int UsedPercent, long? WindowDurationMinutes, long? ResetsAt)
{
    public int RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
    public DateTimeOffset? ResetTime => ResetsAt is { } value
        ? DateTimeOffset.FromUnixTimeSeconds(value).ToLocalTime()
        : null;
}

public sealed record QuotaSnapshot(string? PlanType, QuotaWindow? Primary, QuotaWindow? Secondary);
public sealed record ResetCreditsUpdate(int? AvailableCount);

public sealed class ConnectionResetCredits
{
    private long _generation;

    public int? AvailableCount { get; private set; }

    public bool Begin(long generation)
    {
        _generation = generation;
        return Set(null);
    }

    public bool Update(long generation, int? availableCount) =>
        generation == _generation && Set(availableCount is { } count ? Math.Max(0, count) : null);

    public bool End(long generation) => generation == _generation && Set(null);

    public bool Clear() => Set(null);

    private bool Set(int? value)
    {
        if (AvailableCount == value) return false;
        AvailableCount = value;
        return true;
    }
}

public static class ResetCreditsFormatter
{
    public static string? Format(int? availableCount) => availableCount switch
    {
        null or <= 0 => null,
        > 99 => "99+",
        _ => availableCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
}

public enum ConnectionStatus { Connecting, Connected, Reconnecting, Disconnected }
public enum SpeedMode { Standard, Turbo }
public enum PetSize { Small, Medium, Large }
public enum TooltipStyle { Smooth, Pixel }
public enum UiLanguage { Auto, English, Russian }

public sealed class AppSettings
{
    public PetSize PetSize { get; set; } = PetSize.Medium;
    public TooltipStyle TooltipStyle { get; set; } = TooltipStyle.Smooth;
    public UiLanguage Language { get; set; } = UiLanguage.Auto;
    public bool PetVisible { get; set; } = true;
    public bool ShowOnlyWhenCodexActive { get; set; }
    public bool HideInFullscreen { get; set; }
    public bool LockPosition { get; set; }
    public bool ClickThrough { get; set; }
    public bool ShowHistory { get; set; } = true;
    public bool LaunchAtLogin { get; set; }
    public string? CodexPath { get; set; }
    public Dictionary<string, WindowPosition> Positions { get; set; } = [];
    public Dictionary<string, int> ObjectWeights { get; set; } = new()
    {
        ["space"] = 2, ["nature"] = 1, ["code"] = 1
    };
}

public sealed record WindowPosition(double Left, double Top);
public sealed record HistorySample(DateTimeOffset ObservedAt, int RemainingPercent, long? ResetsAt, bool Gap = false);
