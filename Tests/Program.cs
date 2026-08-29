using System.Text.Json;
using QuotaWisp;

var tests = new (string Name, Action Run)[]
{
    ("remaining quota is clamped", () => Equal(77, new QuotaWindow(23, null, null).RemainingPercent)),
    ("primary and secondary rate limits decode", () =>
    {
        using var json = JsonDocument.Parse("""{"rateLimits":{"planType":"plus","primary":{"usedPercent":23,"windowDurationMins":300,"resetsAt":1900000000},"secondary":{"usedPercent":58,"windowDurationMins":10080}}}""");
        True(CodexAppServerClient.TryParseRateLimits(json.RootElement, out var result));
        Equal(77, result.Primary?.RemainingPercent); Equal(42, result.Secondary?.RemainingPercent);
        Equal("plus", result.PlanType);
    }),
    ("codex bucket overrides legacy bucket", () =>
    {
        using var json = JsonDocument.Parse("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":80}}}}""");
        True(CodexAppServerClient.TryParseRateLimits(json.RootElement, out var result)); Equal(20, result.Primary?.RemainingPercent);
    }),
    ("cmd launcher is non-interactive", () =>
    {
        var info = CodexLocator.CreateStartInfo(@"C:\tools\codex.cmd");
        True(!info.UseShellExecute && info.RedirectStandardInput && info.RedirectStandardOutput && info.CreateNoWindow);
        True(info.Arguments.Contains("call \"C:\\tools\\codex.cmd\" app-server --stdio"));
    }),
    ("localization switches deterministically", () =>
    {
        L.SetLanguage(UiLanguage.Russian); Equal("Выход", L.T("quit"));
        L.SetLanguage(UiLanguage.English); Equal("Quit", L.T("quit"));
    }),
    ("quota moon uses ten-percent buckets", () =>
    {
        Equal(100, QuotaMoon.BucketFor(100)); Equal(100, QuotaMoon.BucketFor(91));
        Equal(90, QuotaMoon.BucketFor(90)); Equal(50, QuotaMoon.BucketFor(41));
        Equal(10, QuotaMoon.BucketFor(10)); Equal(10, QuotaMoon.BucketFor(0));
        Equal("pack://application:,,,/Assets/quota-wisp.png", QuotaMoon.AssetUri(100));
        Equal("pack://application:,,,/Assets/quota-wisp-40.png", QuotaMoon.AssetUri(40));
    }),
    ("replaced Codex connections reject stale callbacks", () =>
    {
        var gate = new ConnectionGenerationGate();
        var first = gate.Advance();
        True(gate.IsCurrent(first));
        var second = gate.Advance();
        True(!gate.IsCurrent(first)); True(gate.IsCurrent(second));
        gate.Invalidate(second);
        True(!gate.IsCurrent(second));
    }),
    ("transparent padding does not capture pointer input", () =>
    {
        var satellites = new[] { new System.Windows.Rect(10, 10, 20, 10) };
        True(PetInputRegion.Contains(new System.Windows.Point(100, 100), new System.Windows.Point(100, 100), 50, satellites));
        True(PetInputRegion.Contains(new System.Windows.Point(15, 15), new System.Windows.Point(100, 100), 50, satellites));
        True(!PetInputRegion.Contains(new System.Windows.Point(5, 90), new System.Windows.Point(100, 100), 50, satellites));
    }),
    ("quota history presents continuous burn", () =>
    {
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var reset = now.AddHours(3).ToUnixTimeSeconds();
        var history = QuotaHistoryPresentation.Create([
            new HistorySample(now.AddHours(-2), 96, reset, false),
            new HistorySample(now.AddHours(-1), 88, reset, false),
            new HistorySample(now, 73, reset, false)
        ], now);
        Equal(3, history.Points.Count); Equal(HistoryBoundary.Continuous, history.Points[1].BoundaryBefore);
        True(history.Summary.Contains("96% → 73%")); True(!history.ContainsGap && !history.ContainsReset);
    }),
    ("quota history marks gaps and resets", () =>
    {
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var history = QuotaHistoryPresentation.Create([
            new HistorySample(now.AddHours(-3), 40, now.AddHours(-2).ToUnixTimeSeconds(), false),
            new HistorySample(now.AddHours(-2), 100, now.AddHours(3).ToUnixTimeSeconds(), false),
            new HistorySample(now, 95, now.AddHours(3).ToUnixTimeSeconds(), true)
        ], now);
        Equal(HistoryBoundary.Reset, history.Points[1].BoundaryBefore);
        Equal(HistoryBoundary.Gap, history.Points[2].BoundaryBefore);
        True(history.ContainsReset && history.ContainsGap);
    }),
    ("pet layouts keep satellites visible outside the moon", () =>
    {
        foreach (var size in Enum.GetValues<PetSize>())
        {
            var layout = PetLayout.For(size);
            var satelliteHalfDiagonal = Math.Sqrt(36 * 36 + 26 * 26) / 2 * layout.SatelliteScale;
            True(layout.OrbitRadius > layout.MoonSize / 2 + 12);
            True(layout.OrbitRadius + satelliteHalfDiagonal < layout.OrbitSize / 2);
            True(layout.WindowHeight > layout.OrbitSize);
        }
    })
};

var failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
if (failures == 0 && args.Contains("--integration"))
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    using var client = new CodexAppServerClient();
    var observed = new TaskCompletionSource<QuotaSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.SnapshotReceived += (_, snapshot) => observed.TrySetResult(snapshot);
    var run = client.RunAsync(null, timeout.Token);
    var completed = await Task.WhenAny(observed.Task, Task.Delay(TimeSpan.FromSeconds(12), timeout.Token));
    if (completed != observed.Task) { Console.Error.WriteLine("FAIL live Codex App Server did not return quota"); failures++; }
    else { Console.WriteLine("PASS live Codex App Server returned quota"); timeout.Cancel(); }
    try { await run; }
    catch (OperationCanceledException) { }
    catch (Exception error) { Console.Error.WriteLine($"Integration process: {error.Message}"); }
}
return failures == 0 ? 0 : 1;

static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
static void Equal<T>(T expected, T actual)
{ if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
