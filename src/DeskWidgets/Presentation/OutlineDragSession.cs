using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DeskWidgets;

internal sealed class OutlineDragSession : IDisposable
{
    private static OutlineDragSession? current;
    internal static bool IsDragging => current != null;
    private readonly int resizeEdge, minimumWidth, minimumHeight;
    private readonly nint target;
    private readonly nint preview;
    private readonly Win32Interop.Rect original;
    private readonly Win32Interop.Point origin;
    private readonly Window outline;
    private readonly Action completed;
    private readonly DispatcherTimer timer = new(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(16) };
    private bool finished;
    internal Win32Interop.Rect PreviewBounds { get; private set; }

    internal OutlineDragSession(Window window, Win32Interop.Point origin, Action completed, int resizeEdge = 0)
    {
        target = new WindowInteropHelper(window).Handle;
        if (!Win32Interop.GetWindowRect(target, out original)) throw new InvalidOperationException("无法读取挂件位置。");
        current?.Dispose();
        this.resizeEdge = resizeEdge;
        var dpi = VisualTreeHelper.GetDpi(window);
        minimumWidth = (int)Math.Ceiling(window.MinWidth * dpi.DpiScaleX); minimumHeight = (int)Math.Ceiling(window.MinHeight * dpi.DpiScaleY);
        this.origin = origin;
        this.completed = completed;
        PreviewBounds = original;
        outline = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false,
            ResizeMode = ResizeMode.NoResize, Topmost = true, IsHitTestVisible = false,
            Left = window.Left, Top = window.Top, Width = window.ActualWidth, Height = window.ActualHeight,
            Content = new Rectangle { Stroke = new SolidColorBrush(Color.FromRgb(125, 211, 252)), StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection([5, 3]), RadiusX = 15, RadiusY = 15, Margin = new Thickness(2), Fill = new SolidColorBrush(Color.FromArgb(12, 125, 211, 252)) }
        };
        outline.SourceInitialized += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(outline).Handle;
            Win32Interop.SetLong(hwnd, Win32Interop.ExStyle, Win32Interop.GetLong(hwnd, Win32Interop.ExStyle) | 0x080000A0);
        };
        outline.Show();
        preview = new WindowInteropHelper(outline).Handle;
        Win32Interop.SetWindowPos(preview, Win32Interop.Topmost, original.Left, original.Top, original.Right - original.Left, original.Bottom - original.Top, 0x10);
        timer.Tick += (_, _) =>
        {
            if (!Win32Interop.GetCursorPos(out var cursor)) { Dispose(); return; }
            // Mouse-up comes from captured input events, not the global physical-button state.
            Update(cursor, true, Win32Interop.GetAsyncKeyState(0x1B) < 0);
        };
        current = this;
        timer.Start();
    }

    internal void Update(Win32Interop.Point cursor, bool pressed, bool cancel = false)
    {
        if (finished) return;
        if (cancel) { Dispose(); return; }
        int dx = cursor.X - origin.X, dy = cursor.Y - origin.Y;
        var bounds = original;
        if (resizeEdge == 0) { bounds.Left += dx; bounds.Right += dx; bounds.Top += dy; bounds.Bottom += dy; }
        else
        {
            if (resizeEdge is 10 or 13 or 16) bounds.Left = Math.Min(original.Right - minimumWidth, original.Left + dx);
            if (resizeEdge is 11 or 14 or 17) bounds.Right = Math.Max(original.Left + minimumWidth, original.Right + dx);
            if (resizeEdge is 12 or 13 or 14) bounds.Top = Math.Min(original.Bottom - minimumHeight, original.Top + dy);
            if (resizeEdge is 15 or 16 or 17) bounds.Bottom = Math.Max(original.Top + minimumHeight, original.Bottom + dy);
        }
        bool changed = bounds.Left != original.Left || bounds.Top != original.Top || bounds.Right != original.Right || bounds.Bottom != original.Bottom;
        if (changed && (Math.Abs(dx) >= 4 || Math.Abs(dy) >= 4 || !PreviewBounds.Equals(original)))
        {
            if (!bounds.Equals(PreviewBounds))
            {
                PreviewBounds = bounds;
                Win32Interop.SetWindowPos(preview, 0, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x14);
            }
        }
        else if (!changed && !PreviewBounds.Equals(original))
        {
            PreviewBounds = original;
            Win32Interop.SetWindowPos(preview, 0, original.Left, original.Top, original.Right - original.Left, original.Bottom - original.Top, 0x14);
        }
        if (!pressed)
        {
            if (!PreviewBounds.Equals(original))
                Win32Interop.SetWindowPos(target, 0, PreviewBounds.Left, PreviewBounds.Top, PreviewBounds.Right - PreviewBounds.Left, PreviewBounds.Bottom - PreviewBounds.Top, resizeEdge == 0 ? 0x15u : 0x14u);
            Dispose();
        }
    }

    public void Dispose()
    {
        if (finished) return;
        finished = true; timer.Stop();
        if (ReferenceEquals(current, this)) current = null;
        outline.Close(); completed();
    }
}
