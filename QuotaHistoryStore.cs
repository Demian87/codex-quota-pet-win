using System.Text.Json;
using System.IO;

namespace QuotaWisp;

public sealed class QuotaHistoryStore
{
    private readonly string _path = Path.Combine(SettingsStore.DataDirectory, "quota-history-v1.json");
    private readonly List<HistorySample> _samples = [];
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromHours(1);
    public IReadOnlyList<HistorySample> Samples => _samples;

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
                _samples.AddRange(JsonSerializer.Deserialize<List<HistorySample>>(File.ReadAllText(_path)) ?? []);
        }
        catch { }
        Prune();
    }

    public void Add(QuotaSnapshot snapshot, bool gap)
    {
        if (snapshot.Primary is not { } primary) return;
        var sample = new HistorySample(DateTimeOffset.Now, primary.RemainingPercent, primary.ResetsAt, gap);
        if (_samples.LastOrDefault() is { } last && !gap && last.RemainingPercent == sample.RemainingPercent &&
            last.ResetsAt == sample.ResetsAt && sample.ObservedAt - last.ObservedAt < Heartbeat) return;
        _samples.Add(sample);
        Prune(); Persist();
    }

    public void Clear() { _samples.Clear(); Persist(); }

    public QuotaHistoryPresentation Presentation(DateTimeOffset? now = null) =>
        QuotaHistoryPresentation.Create(_samples, now ?? DateTimeOffset.Now);

    private void Prune()
    {
        _samples.RemoveAll(x => DateTimeOffset.Now - x.ObservedAt > Retention);
        if (_samples.Count > 2000) _samples.RemoveRange(0, _samples.Count - 2000);
    }

    private void Persist()
    {
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_samples));
        File.Move(temporary, _path, true);
    }
}
