using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace DeskWidgets;
internal sealed class FullscreenMonitor : IDisposable
{
    private readonly Action<bool> changed;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int fullSamples;
    private bool sleeping;
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public Win32Interop.Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Win32Interop.Rect bounds, int size);
    internal FullscreenMonitor(Action<bool> changed, bool enabled)
    {
        this.changed = changed; timer.Tick += (_, _) => Check(); if (enabled) timer.Start();
    }
    internal static bool CoversMonitor(Win32Interop.Rect window, Win32Interop.Rect monitor, bool caption, bool maximized)
    {
        if (caption && maximized) return false;
        return monitor.Right > monitor.Left && monitor.Bottom > monitor.Top && Math.Abs((long)window.Left - monitor.Left) <= 2 && Math.Abs((long)window.Top - monitor.Top) <= 2 && Math.Abs((long)window.Right - monitor.Right) <= 2 && Math.Abs((long)window.Bottom - monitor.Bottom) <= 2;
    }
    private static bool Detect()
    {
        nint hwnd = GetForegroundWindow(); if (hwnd == 0 || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out uint process); if (process == Environment.ProcessId) return false;
        var name = new StringBuilder(128); GetClassName(hwnd, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return false;
        if (DwmGetWindowAttribute(hwnd, 9, out var bounds, Marshal.SizeOf<Win32Interop.Rect>()) != 0 && !Win32Interop.GetWindowRect(hwnd, out bounds)) return false;
        return CoversMonitor(bounds, info.Monitor, (Win32Interop.GetLong(hwnd, -16) & 0x00C00000) != 0, IsZoomed(hwnd));
    }
    internal void Check()
    {
        bool full = Detect(); fullSamples = full ? Math.Min(2, fullSamples + 1) : 0;
        bool next = full && (sleeping || fullSamples >= 2);
        if (next == sleeping) return; sleeping = next; changed(next);
    }
    public void Dispose() => timer.Stop();
}
