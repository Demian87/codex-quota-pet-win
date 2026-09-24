using System.IO;

namespace QuotaWisp;

public sealed record ProcessTreeEntry(uint ProcessId, uint ParentProcessId, string Name);

public static class CodexForegroundDetector
{
    private static readonly HashSet<string> TerminalHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "windowsterminal", "wt", "openconsole", "conhost", "cmd", "powershell", "pwsh",
        "wezterm-gui", "alacritty", "kitty", "mintty"
    };

    public static bool IsCodexForeground(
        uint foregroundProcessId,
        string foregroundProcessName,
        string foregroundTitle,
        IReadOnlyCollection<ProcessTreeEntry> processes)
    {
        if (IsCodexProcess(foregroundProcessName)) return true;
        if (!TerminalHosts.Contains(Normalize(foregroundProcessName))) return false;

        var children = processes.GroupBy(process => process.ParentProcessId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var pending = new Queue<uint>();
        var visited = new HashSet<uint> { foregroundProcessId };
        pending.Enqueue(foregroundProcessId);
        while (pending.TryDequeue(out var parent))
        {
            if (!children.TryGetValue(parent, out var descendants)) continue;
            foreach (var descendant in descendants)
            {
                if (!visited.Add(descendant.ProcessId)) continue;
                if (IsCodexProcess(descendant.Name)) return true;
                pending.Enqueue(descendant.ProcessId);
            }
        }

        // Some terminal hosts keep the pseudoconsole outside their process subtree.
        // Their visible title is the least invasive fallback available on Windows.
        return foregroundTitle.Contains("codex", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCodexProcess(string name)
    {
        var normalized = Normalize(name);
        return normalized.Equals("codex", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("codex-", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("codex.", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("codex ", StringComparison.OrdinalIgnoreCase);
    }
    private static string Normalize(string name) => Path.GetFileNameWithoutExtension(name.Trim());
}
