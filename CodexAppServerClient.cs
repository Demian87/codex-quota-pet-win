using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace QuotaWisp;

public sealed class CodexAppServerClient : IDisposable
{
    private Process? _process;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _requestId;
    private CancellationToken _token;

    public event EventHandler<QuotaSnapshot>? SnapshotReceived;
    public event EventHandler<SpeedMode>? SpeedModeReceived;

    public async Task RunAsync(string? customPath, CancellationToken token)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(token);
        var runToken = run.Token;
        _token = runToken;
        var executable = CodexLocator.Find(customPath) ?? throw new FileNotFoundException(
            "Codex executable was not found. Select it from the tray menu.");
        var startInfo = CodexLocator.CreateStartInfo(executable);
        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!_process.Start()) throw new InvalidOperationException("Codex App Server could not be started.");
        var errorOutput = _process.StandardError.ReadToEndAsync(token);

        try
        {
            await SendAsync(new { method = "initialize", id = 0, @params = new {
                clientInfo = new { name = "quota_wisp_windows", title = "Quota Wisp", version = "1.0.0" }
            } });
            await SendAsync(new { method = "initialized", @params = new { } });
            await RequestRateLimitsAsync();
            await RequestConfigAsync();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            string details;
            try { details = await errorOutput; } catch { details = ""; }
            throw new IOException("Codex App Server handshake failed." +
                (string.IsNullOrWhiteSpace(details) ? "" : $" {details.Trim()}"), error);
        }

        var reader = ReadOutputAsync(runToken);
        var polling = PollAsync(runToken);
        var exited = _process.WaitForExitAsync(runToken);
        await Task.WhenAny(reader, exited);
        run.Cancel();
        try { await polling; } catch (OperationCanceledException) { }
        if (!token.IsCancellationRequested)
        {
            string details;
            try { details = await errorOutput; } catch { details = ""; }
            throw new IOException("Codex App Server stopped unexpectedly." +
                (string.IsNullOrWhiteSpace(details) ? "" : $" {details.Trim()}"));
        }
    }

    public Task RefreshAsync() => _process is { HasExited: false }
        ? RequestRateLimitsAsync() : Task.CompletedTask;

    private async Task PollAsync(CancellationToken token)
    {
        var quotaTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        var configTimer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        var quotaTask = Task.Run(async () => {
            while (await quotaTimer.WaitForNextTickAsync(token)) await RequestRateLimitsAsync();
        }, token);
        var configTask = Task.Run(async () => {
            while (await configTimer.WaitForNextTickAsync(token)) await RequestConfigAsync();
        }, token);
        try { await Task.WhenAll(quotaTask, configTask); } catch (OperationCanceledException) { }
        quotaTimer.Dispose(); configTimer.Dispose();
    }

    private async Task ReadOutputAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _process is { HasExited: false })
        {
            var line = await _process.StandardOutput.ReadLineAsync(token);
            if (line is null) break;
            HandleMessage(line);
        }
    }

    private void HandleMessage(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("method", out var method) &&
                method.GetString() == "account/rateLimits/updated")
            {
                _ = RefreshAsync(); return;
            }
            if (!root.TryGetProperty("result", out var result)) return;

            if (TryParseRateLimits(result, out var snapshot)) SnapshotReceived?.Invoke(this, snapshot);
            else if (result.TryGetProperty("config", out var config))
            {
                var tier = GetString(config, "service_tier")?.ToLowerInvariant();
                SpeedModeReceived?.Invoke(this, tier is "fast" or "priority" ? SpeedMode.Turbo : SpeedMode.Standard);
            }
        }
        catch (JsonException) { }
    }

    public static bool TryParseRateLimits(JsonElement result, out QuotaSnapshot snapshot)
    {
        snapshot = new(null, null, null);
        if (!result.TryGetProperty("rateLimits", out var limits)) return false;
        if (result.TryGetProperty("rateLimitsByLimitId", out var byId) &&
            byId.ValueKind == JsonValueKind.Object && byId.TryGetProperty("codex", out var codex)) limits = codex;
        snapshot = new QuotaSnapshot(
            GetString(limits, "planType"), ParseWindow(limits, "primary"), ParseWindow(limits, "secondary"));
        return true;
    }

    private static QuotaWindow? ParseWindow(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return new QuotaWindow(GetInt(value, "usedPercent") ?? 0,
            GetLong(value, "windowDurationMins"), GetLong(value, "resetsAt"));
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : null;
    private static long? GetLong(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : null;

    private Task RequestRateLimitsAsync() => SendAsync(new { method = "account/rateLimits/read", id = NextId() });
    private Task RequestConfigAsync() => SendAsync(new {
        method = "config/read", id = NextId(), @params = new { includeLayers = false }
    });
    private int NextId() => Interlocked.Increment(ref _requestId);

    private async Task SendAsync(object message)
    {
        if (_process is not { HasExited: false }) return;
        var line = JsonSerializer.Serialize(message);
        await _writeLock.WaitAsync(_token);
        try { await _process.StandardInput.WriteLineAsync(line); await _process.StandardInput.FlushAsync(); }
        finally { _writeLock.Release(); }
    }

    public void Dispose()
    {
        try { if (_process is { HasExited: false }) _process.Kill(true); } catch { }
        _process?.Dispose(); _writeLock.Dispose();
    }
}

public static class CodexLocator
{
    public static string? Find(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath)) return customPath;
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.ps1"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Codex", "codex.exe")
        };
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            candidates.Add(Path.Combine(folder.Trim('"'), "codex.exe"));
            candidates.Add(Path.Combine(folder.Trim('"'), "codex.cmd"));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    public static ProcessStartInfo CreateStartInfo(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var info = new ProcessStartInfo {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(path)!
        };
        if (extension is ".cmd" or ".bat")
        {
            info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            info.Arguments = $"/d /s /c call \"{path}\" app-server --stdio";
        }
        else if (extension == ".ps1")
        {
            info.FileName = "powershell.exe";
            info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-ExecutionPolicy");
            info.ArgumentList.Add("Bypass"); info.ArgumentList.Add("-File"); info.ArgumentList.Add(path);
            info.ArgumentList.Add("app-server"); info.ArgumentList.Add("--stdio");
        }
        else { info.FileName = path; info.ArgumentList.Add("app-server"); info.ArgumentList.Add("--stdio"); }
        return info;
    }
}
