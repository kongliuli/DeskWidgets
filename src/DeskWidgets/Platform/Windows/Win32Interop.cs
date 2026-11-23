using System;
using System.Runtime.InteropServices;

namespace DeskWidgets;

internal static class Win32Interop
{
    internal const int ExStyle = -20, Owner = -8;
    internal static readonly nint Bottom = 1, Topmost = -1, NotTopmost = -2;
    [StructLayout(LayoutKind.Sequential)]
    private struct QueueOptions { public int Size, ThreadType, ApartmentType; }
    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(QueueOptions options, out nint controller);
    internal static Windows.System.DispatcherQueueController? CreateCompositionQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() != null) return null;
        var options = new QueueOptions { Size = Marshal.SizeOf<QueueOptions>(), ThreadType = 2, ApartmentType = 2 };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out var pointer));
        try { return Windows.System.DispatcherQueueController.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPos { public nint Hwnd, InsertAfter; public int X, Y, Cx, Cy; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern uint GetGuiResources(nint process, uint flags);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetLong64(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetLong32(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetLong64(nint hwnd, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetLong32(nint hwnd, int index, int value);
    internal static nint GetLong(nint hwnd, int index) => IntPtr.Size == 8 ? GetLong64(hwnd, index) : GetLong32(hwnd, index);
    internal static void SetLong(nint hwnd, int index, nint value)
    { if (IntPtr.Size == 8) SetLong64(hwnd, index, value); else SetLong32(hwnd, index, (int)value); }
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindow(string name, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent, nint after, string name, string? title);
    private delegate bool EnumProc(nint hwnd, nint param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint param);
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    internal static void EnsureOnScreen(nint hwnd)
    {
        if (MonitorFromWindow(hwnd, 0) != 0) return;
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info))
            SetWindowPos(hwnd, 0, info.Work.Left + 32, info.Work.Top + 32, 0, 0, 0x15);
    }
    internal static nint DesktopOwner()
    {
        nint desktop = FindWindow("Progman", null);
        EnumWindows((hwnd, _) =>
        {
            if (FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null) == 0) return true;
            desktop = hwnd;
            return false;
        }, 0);
        return desktop;
    }
}
