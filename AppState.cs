namespace QuotaWisp;

public sealed class AppState : IDisposable
{
    private readonly SettingsStore _settingsStore = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConnectionGenerationGate _connectionGeneration = new();
    private readonly ConnectionResetCredits _resetCredits = new();
    private CodexAppServerClient? _client;
    private bool _started;
    private bool _historyGap = true;
    private int? _previousRemaining;
    private bool _clientReceivedSnapshot;

    public AppSettings Settings { get; }
    public QuotaHistoryStore History { get; } = new();
    public QuotaSnapshot? Quota { get; private set; }
    public SpeedMode SpeedMode { get; private set; } = SpeedMode.Standard;
    public ConnectionStatus Connection { get; private set; } = ConnectionStatus.Connecting;
    public string? LastError { get; private set; }
    public int? ResetCreditsAvailableCount => _resetCredits.AvailableCount;

    public event EventHandler? Changed;
    public event EventHandler? TrayRefreshRequested;
    public event EventHandler<int>? QuotaConsumed;
    public event EventHandler<string>? LowQuotaNotification;

    public AppState()
    {
        Settings = _settingsStore.Load();
        Settings.LaunchAtLogin = SettingsStore.IsAutoStartEnabled();
        L.SetLanguage(Settings.Language);
        History.Load();
    }

    public Task StartAsync()
    {
        if (_started) return Task.CompletedTask;
        _started = true;
        return Task.Run(() => ConnectionLoopAsync(_lifetime.Token));
    }

    private async Task ConnectionLoopAsync(CancellationToken token)
    {
        var delays = new[] { 1, 2, 5, 10, 30 };
        var attempt = 0;
        while (!token.IsCancellationRequested)
        {
            SetConnection(attempt == 0 ? ConnectionStatus.Connecting : ConnectionStatus.Reconnecting, null);
            var generation = _connectionGeneration.Advance();
            EventHandler<QuotaSnapshot>? snapshotHandler = null;
            EventHandler<SpeedMode>? speedModeHandler = null;
            EventHandler<ResetCreditsUpdate>? resetCreditsHandler = null;
            CodexAppServerClient? client = null;
            try
            {
                _clientReceivedSnapshot = false;
                if (_resetCredits.Begin(generation)) RaiseChanged();
                client = new CodexAppServerClient();
                _client = client;
                snapshotHandler = (_, snapshot) =>
                {
                    if (_connectionGeneration.IsCurrent(generation)) OnSnapshot(client, snapshot);
                };
                speedModeHandler = (_, mode) =>
                {
                    if (_connectionGeneration.IsCurrent(generation)) OnSpeedMode(client, mode);
                };
                resetCreditsHandler = (_, update) =>
                {
                    if (_connectionGeneration.IsCurrent(generation) &&
                        _resetCredits.Update(generation, update.AvailableCount)) RaiseChanged();
                };
                client.SnapshotReceived += snapshotHandler;
                client.SpeedModeReceived += speedModeHandler;
                client.ResetCreditsReceived += resetCreditsHandler;
                await client.RunAsync(Settings.CodexPath, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                _historyGap = true;
                SetConnection(ConnectionStatus.Reconnecting, error.Message);
            }
            finally
            {
                _connectionGeneration.Invalidate(generation);
                if (_resetCredits.End(generation)) RaiseChanged();
                if (client is not null)
                {
                    if (snapshotHandler is not null) client.SnapshotReceived -= snapshotHandler;
                    if (speedModeHandler is not null) client.SpeedModeReceived -= speedModeHandler;
                    if (resetCreditsHandler is not null) client.ResetCreditsReceived -= resetCreditsHandler;
                    client.Dispose();
                }
                if (ReferenceEquals(_client, client)) _client = null;
            }
            if (token.IsCancellationRequested) break;
            if (_clientReceivedSnapshot) attempt = 0;
            await Task.Delay(TimeSpan.FromSeconds(delays[Math.Min(attempt, delays.Length - 1)]), token);
            attempt++;
        }
        SetConnection(ConnectionStatus.Disconnected, LastError);
    }

    private void OnSnapshot(object? sender, QuotaSnapshot snapshot)
    {
        _clientReceivedSnapshot = true;
        var remaining = snapshot.Primary?.RemainingPercent;
        if (_previousRemaining is { } previous && remaining is { } current && current < previous)
        {
            QuotaConsumed?.Invoke(this, previous - current);
            if (previous > 20 && current <= 20)
                LowQuotaNotification?.Invoke(this, string.Format(L.T("lowquota"), current));
        }
        _previousRemaining = remaining;
        Quota = snapshot;
        History.Add(snapshot, _historyGap);
        _historyGap = false;
        if (Connection == ConnectionStatus.Connected && LastError is null) RaiseChanged();
        else SetConnection(ConnectionStatus.Connected, null);
    }

    private void OnSpeedMode(object? sender, SpeedMode mode)
    {
        if (SpeedMode == mode) return;
        SpeedMode = mode; RaiseChanged();
    }

    private void SetConnection(ConnectionStatus status, string? error)
    {
        if (Connection == status && LastError == error) return;
        Connection = status; LastError = error; RaiseChanged();
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        TrayRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    public Task RefreshAsync() => _client?.RefreshAsync() ?? Task.CompletedTask;
    public void SetPetVisible(bool value) { Settings.PetVisible = value; SaveAndRaise(); }
    public void SetPetSize(PetSize value) { Settings.PetSize = value; SaveAndRaise(); }
    public void SetTooltipStyle(TooltipStyle value) { Settings.TooltipStyle = value; SaveAndRaise(); }
    public void SetLockPosition(bool value) { Settings.LockPosition = value; SaveAndRaise(); }
    public void SetClickThrough(bool value) { Settings.ClickThrough = value; SaveAndRaise(); }
    public void SetShowOnlyWhenCodexActive(bool value) { Settings.ShowOnlyWhenCodexActive = value; SaveAndRaise(); }
    public void SetHideInFullscreen(bool value) { Settings.HideInFullscreen = value; SaveAndRaise(); }
    public void SetShowHistory(bool value) { Settings.ShowHistory = value; SaveAndRaise(); }
    public void SetLanguage(UiLanguage value) { Settings.Language = value; L.SetLanguage(value); SaveAndRaise(); }

    public void SetAutoStart(bool value)
    {
        try { SettingsStore.SetAutoStart(value); Settings.LaunchAtLogin = value; LastError = null; }
        catch (Exception error) { LastError = error.Message; Settings.LaunchAtLogin = SettingsStore.IsAutoStartEnabled(); }
        SaveAndRaise();
    }

    public void SetCodexPath(string path)
    {
        Settings.CodexPath = path; SaveAndRaise();
        _connectionGeneration.Advance();
        if (_resetCredits.Clear()) RaiseChanged();
        _client?.Dispose();
    }

    public void SetObjectWeight(string category, int weight)
    {
        Settings.ObjectWeights[category] = Math.Clamp(weight, 0, 3);
        if (Settings.ObjectWeights.Values.All(x => x == 0)) Settings.ObjectWeights[category] = 1;
        SaveAndRaise();
    }

    public void SavePosition(string screenKey, double left, double top)
    {
        Settings.Positions[screenKey] = new WindowPosition(left, top); _settingsStore.Save(Settings);
    }

    public void ClearHistory() { History.Clear(); RaiseChanged(); }
    public async Task FlushHistoryForUpdateAsync(TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        await History.FlushAsync(timeoutSource.Token).WaitAsync(timeout, timeoutSource.Token);
    }
    private void SaveAndRaise() { _settingsStore.Save(Settings); RaiseChanged(); }

    public void Dispose()
    {
        _connectionGeneration.Advance();
        _resetCredits.Clear();
        _lifetime.Cancel(); _client?.Dispose(); _lifetime.Dispose();
    }
}
