using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DeskWidgets;
internal sealed class DesktopNoteWindow : Window
{
    private readonly SavedNote state;
    private bool sleeping;
    internal void SetSleeping(bool value) { sleeping = value; if (value) Hide(); else { Show(); ApplyLayer(true); } }
    internal DesktopNoteWindow(SavedNote state)
    {
        this.state = state; Title = "DeskWidgets 桌面便签";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Focusable = false; IsHitTestVisible = false;
        MinWidth = 240; MinHeight = 140; Width = state.Width; Height = state.Height;
        var text = new TextBlock { Text = state.Text, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(242, 230, 199)), FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
        var panel = new DockPanel(); var label = new TextBlock { Text = state.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm") + "   ·   随手记", Foreground = new SolidColorBrush(Color.FromRgb(180, 167, 139)), FontSize = 10, Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label); panel.Children.Add(text);
        Content = new Border { Padding = new Thickness(18), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(65, 230, 206, 150)), Background = new SolidColorBrush(Color.FromArgb(205, 36, 34, 29)), Child = panel, ClipToBounds = true };
        SourceInitialized += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            Win32Interop.SetLong(hwnd, Win32Interop.ExStyle, Win32Interop.GetLong(hwnd, Win32Interop.ExStyle) | 0x080000A0);
            HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
            ApplyLayer(); Win32Interop.SetWindowPos(hwnd, 0, state.X, state.Y, 0, 0, 0x15); Win32Interop.EnsureOnScreen(hwnd);
        };
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x46 && !sleeping) { var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32Interop.WindowPos>(lParam); if ((pos.Flags & 0x80) != 0) { pos.Flags &= ~0x80u; System.Runtime.InteropServices.Marshal.StructureToPtr(pos, lParam, false); } }
        if (message == 0x84) { handled = true; return -1; }
        if (message == 0x21) { handled = true; return 3; }
        if (message == 0x112 && ((long)wParam & 0xfff0) is 0xf020 or 0xf060) handled = true;
        return 0;
    }
    internal void ApplyLayer(bool force = false)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        var owner = Win32Interop.DesktopOwner();
        if (!force && Win32Interop.GetLong(hwnd, Win32Interop.Owner) == owner) return;
        Win32Interop.SetLong(hwnd, Win32Interop.Owner, owner);
        Win32Interop.SetWindowPos(hwnd, Win32Interop.Bottom, 0, 0, 0, 0, 0x13);
    }
    internal void MoveToCursor()
    {
        if (Win32Interop.GetCursorPos(out var cursor)) Win32Interop.SetWindowPos(new WindowInteropHelper(this).Handle, 0, cursor.X + 12, cursor.Y + 12, 0, 0, 0x15);
    }
    internal void CaptureState()
    {
        if (Win32Interop.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect)) { state.X = rect.Left; state.Y = rect.Top; }
        state.Width = Width; state.Height = Height;
    }
}
