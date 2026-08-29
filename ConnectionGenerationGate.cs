namespace QuotaWisp;

/// <summary>
/// Invalidates callbacks that belong to a Codex App Server process which has
/// already stopped or been replaced.
/// </summary>
public sealed class ConnectionGenerationGate
{
    private long _generation;

    public long Advance() => Interlocked.Increment(ref _generation);

    public bool IsCurrent(long generation) =>
        Volatile.Read(ref _generation) == generation;

    public void Invalidate(long generation) =>
        Interlocked.CompareExchange(ref _generation, generation + 1, generation);
}
