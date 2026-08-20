using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace QuotaWisp;

public static class Win32
{
    private const int GwlExStyle = -20, WsExTransparent = 0x20, WsExNoActivate = 0x08000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int value);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

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
}
