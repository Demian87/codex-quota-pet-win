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
    ("manual reset credits decode direct and enveloped payloads", () =>
    {
        using var direct = JsonDocument.Parse("""{"rateLimits":{},"rateLimitResetCredits":{"availableCount":7}}""");
        True(CodexAppServerClient.TryParseResetCredits(direct.RootElement, out var directCount));
        Equal(7, directCount);
        using var enveloped = JsonDocument.Parse("""{"result":{"data":{"rateLimitResetCredits":{"availableCount":123}}}}""");
        True(CodexAppServerClient.TryParseResetCredits(enveloped.RootElement, out var envelopeCount));
        Equal(123, envelopeCount);
        using var fullEnvelope = JsonDocument.Parse("""{"data":{"rateLimits":{"primary":{"usedPercent":25}},"rateLimitResetCredits":{"availableCount":4}}}""");
        True(CodexAppServerClient.TryParseRateLimits(fullEnvelope.RootElement, out var envelopeQuota));
        Equal(75, envelopeQuota.Primary?.RemainingPercent);
        using var absent = JsonDocument.Parse("""{"rateLimits":{}}""");
        True(!CodexAppServerClient.TryParseResetCredits(absent.RootElement, out _));
    }),
    ("manual reset credits clamp and format", () =>
    {
        using var negative = JsonDocument.Parse("""{"rateLimitResetCredits":{"availableCount":-4}}""");
        True(CodexAppServerClient.TryParseResetCredits(negative.RootElement, out var clamped));
        Equal(0, clamped);
        Equal<string?>(null, ResetCreditsFormatter.Format(null));
        Equal<string?>(null, ResetCreditsFormatter.Format(0));
        Equal("1", ResetCreditsFormatter.Format(1));
        Equal("99", ResetCreditsFormatter.Format(99));
        Equal("99+", ResetCreditsFormatter.Format(100));
    }),
    ("manual reset credits are scoped to current connection", () =>
    {
        var credits = new ConnectionResetCredits();
        True(!credits.Begin(10));
        True(credits.Update(10, 8)); Equal<int?>(8, credits.AvailableCount);
        True(credits.Begin(11)); Equal<int?>(null, credits.AvailableCount);
        True(!credits.Update(10, 50)); Equal<int?>(null, credits.AvailableCount);
        True(credits.Update(11, 120)); Equal<int?>(120, credits.AvailableCount);
        True(!credits.End(10)); Equal<int?>(120, credits.AvailableCount);
        True(credits.End(11)); Equal<int?>(null, credits.AvailableCount);
    }),
    ("cmd launcher is non-interactive", () =>
    {
        var info = CodexLocator.CreateStartInfo(@"C:\tools\codex.cmd");
        True(!info.UseShellExecute && info.RedirectStandardInput && info.RedirectStandardOutput && info.CreateNoWindow);
        True(info.Arguments.Contains("call \"C:\\tools\\codex.cmd\" app-server --stdio"));
    }),
    ("localization switches deterministically", () =>
    {
        L.SetLanguage(UiLanguage.Russian); Equal("Выход", L.T("quit")); Equal("РУЧНЫЕ СБРОСЫ", L.T("resetcredits"));
        L.SetLanguage(UiLanguage.English); Equal("Quit", L.T("quit")); Equal("MANUAL RESETS", L.T("resetcredits"));
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
    ("pet visibility respects manual fullscreen and active-Codex suppression", () =>
    {
        True(PetVisibilityPolicy.ShouldShow(true, false, false, false));
        True(!PetVisibilityPolicy.ShouldShow(false, false, false, true));
        True(!PetVisibilityPolicy.ShouldShow(true, true, false, true));
        True(!PetVisibilityPolicy.ShouldShow(true, false, true, false));
        True(PetVisibilityPolicy.ShouldShow(true, false, true, true));
    }),
    ("Codex desktop and terminal child processes count as active", () =>
    {
        True(CodexForegroundDetector.IsCodexForeground(10, "Codex.exe", "", Array.Empty<ProcessTreeEntry>()));
        var terminalTree = new[]
        {
            new ProcessTreeEntry(20, 10, "OpenConsole.exe"),
            new ProcessTreeEntry(30, 20, "pwsh.exe"),
            new ProcessTreeEntry(40, 30, "codex.exe")
        };
        True(CodexForegroundDetector.IsCodexForeground(10, "WindowsTerminal.exe", "PowerShell", terminalTree));
        True(CodexForegroundDetector.IsCodexForeground(10, "pwsh.exe", "Codex", Array.Empty<ProcessTreeEntry>()));
        True(!CodexForegroundDetector.IsCodexForeground(10, "pwsh.exe", "PowerShell", Array.Empty<ProcessTreeEntry>()));
        True(!CodexForegroundDetector.IsCodexForeground(10, "devenv.exe", "codex project", terminalTree));
    }),
    ("delayed consumption callbacks remain suppressed while pet is hidden", () =>
    {
        var manuallyVisibleAtQueueTime = true;
        var manuallyVisibleAtExecutionTime = false;
        True(PetVisibilityPolicy.ShouldShow(manuallyVisibleAtQueueTime, false, false, true));
        True(!PetVisibilityPolicy.ShouldShow(manuallyVisibleAtExecutionTime, false, false, true));
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
    }),
    ("semantic versions follow release precedence", () =>
    {
        True(SemanticVersion.TryParse("v1.2.3", out var stable));
        True(SemanticVersion.TryParse("1.2.3-rc.2+build.7", out var candidate));
        True(SemanticVersion.TryParse("1.2.3-rc.10", out var laterCandidate));
        True(stable.CompareTo(candidate) > 0);
        True(laterCandidate.CompareTo(candidate) > 0);
        True(!SemanticVersion.TryParse("1.02.3", out _));
    }),
    ("release checksum is strict and file-bound", () =>
    {
        var hash = new string('a', 64);
        Equal(hash.ToUpperInvariant(), UpdateManager.ParseChecksum($"{hash}  QuotaWisp-win-x64.zip\n", "QuotaWisp-win-x64.zip"));
        Throws<InvalidDataException>(() => UpdateManager.ParseChecksum($"{hash}  another.zip", "QuotaWisp-win-x64.zip"));
        Throws<InvalidDataException>(() => UpdateManager.ParseChecksum($"{hash}  QuotaWisp-win-x64.zip\n{hash}  QuotaWisp-win-x64.zip", "QuotaWisp-win-x64.zip"));
    }),
    ("update archives reject zip slip", () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "QuotaWispSelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archivePath = Path.Combine(root, "bad.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(archivePath, System.IO.Compression.ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open());
                writer.Write("not allowed");
            }
            Throws<InvalidDataException>(() => SafeZipExtractor.Extract(archivePath, Path.Combine(root, "payload")));
            True(!File.Exists(Path.Combine(root, "outside.txt")));
        }
        finally { Directory.Delete(root, true); }
    }),
    ("update installer replaces payload files", () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "QuotaWispInstallTest-" + Guid.NewGuid().ToString("N"));
        var payload = Path.Combine(root, "payload");
        var install = Path.Combine(root, "install");
        Directory.CreateDirectory(Path.Combine(payload, "data"));
        Directory.CreateDirectory(install);
        try
        {
            File.WriteAllText(Path.Combine(install, "QuotaWisp.exe"), "old");
            File.WriteAllText(Path.Combine(payload, "QuotaWisp.exe"), "new");
            File.WriteAllText(Path.Combine(payload, "data", "version.txt"), "1.2.3");
            UpdateBootstrapper.InstallWithRollback(payload, install);
            Equal("new", File.ReadAllText(Path.Combine(install, "QuotaWisp.exe")));
            Equal("1.2.3", File.ReadAllText(Path.Combine(install, "data", "version.txt")));
            True(!Directory.GetDirectories(install, ".quotawisp-rollback-*", SearchOption.TopDirectoryOnly).Any());
        }
        finally { Directory.Delete(root, true); }
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
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
