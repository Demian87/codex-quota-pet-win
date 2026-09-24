using System.Text.Json;
using System.IO;

namespace QuotaWisp;

public sealed class QuotaHistoryStore
{
    private readonly object _sync = new();
    private readonly string _path = Path.Combine(SettingsStore.DataDirectory, "quota-history-v1.json");
    private readonly List<HistorySample> _samples = [];
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromHours(1);
    public IReadOnlyList<HistorySample> Samples { get { lock (_sync) return _samples.ToArray(); } }

    public void Load()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(_path))
                    _samples.AddRange(JsonSerializer.Deserialize<List<HistorySample>>(File.ReadAllText(_path)) ?? []);
            }
            catch { }
            Prune();
        }
    }

    public void Add(QuotaSnapshot snapshot, bool gap)
    {
        if (snapshot.Primary is not { } primary) return;
        lock (_sync)
        {
            var sample = new HistorySample(DateTimeOffset.Now, primary.RemainingPercent, primary.ResetsAt, gap);
            if (_samples.LastOrDefault() is { } last && !gap && last.RemainingPercent == sample.RemainingPercent &&
                last.ResetsAt == sample.ResetsAt && sample.ObservedAt - last.ObservedAt < Heartbeat) return;
            _samples.Add(sample);
            Prune(); Persist();
        }
    }

    public void Clear() { lock (_sync) { _samples.Clear(); Persist(); } }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync) Persist();
    }, cancellationToken);

    public QuotaHistoryPresentation Presentation(DateTimeOffset? now = null)
    {
        lock (_sync) return QuotaHistoryPresentation.Create(_samples.ToArray(), now ?? DateTimeOffset.Now);
    }

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
