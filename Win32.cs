using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using System.Windows.Interop;

namespace QuotaWisp;

public static class Win32
{
    private const int GwlExStyle = -20, WsExTransparent = 0x20, WsExNoActivate = 0x08000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int value);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maximum);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId; public IntPtr DefaultHeapId; public uint ModuleId, Threads, ParentProcessId;
        public int BasePriority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExecutableFile;
    }

    public static void SetClickThrough(System.Windows.Window window, bool enabled)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLong(handle, GwlExStyle) | WsExNoActivate;
        SetWindowLong(handle, GwlExStyle, enabled ? style | WsExTransparent : style & ~WsExTransparent);
    }

    public static bool IsForegroundFullscreen(IntPtr ownHandle)
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || window == ownHandle || !GetWindowRect(window, out var rect)) return false;
        var monitor = MonitorFromWindow(window, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        const int tolerance = 2;
        return Math.Abs(rect.Left - info.Monitor.Left) <= tolerance && Math.Abs(rect.Top - info.Monitor.Top) <= tolerance &&
               Math.Abs(rect.Right - info.Monitor.Right) <= tolerance && Math.Abs(rect.Bottom - info.Monitor.Bottom) <= tolerance;
    }

    public static bool IsCodexForeground(IntPtr ownHandle)
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || window == ownHandle) return false;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return false;
        string processName;
        try { processName = Process.GetProcessById((int)processId).ProcessName; }
        catch { return false; }
        var title = new StringBuilder(512);
        GetWindowText(window, title, title.Capacity);
        return CodexForegroundDetector.IsCodexForeground(processId, processName, title.ToString(), SnapshotProcesses());
    }

    private static IReadOnlyCollection<ProcessTreeEntry> SnapshotProcesses()
    {
        const uint snapshotProcesses = 0x00000002;
        var snapshot = CreateToolhelp32Snapshot(snapshotProcesses, 0);
        if (snapshot == new IntPtr(-1)) return Array.Empty<ProcessTreeEntry>();
        var result = new List<ProcessTreeEntry>();
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32FirstW(snapshot, ref entry)) return result;
            do
            {
                result.Add(new ProcessTreeEntry(entry.ProcessId, entry.ParentProcessId, entry.ExecutableFile ?? ""));
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry>();
            } while (Process32NextW(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return result;
    }
}
