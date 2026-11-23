using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;

namespace DeskWidgets;
internal static class DesktopLayout
{
    internal static List<Win32Interop.Rect> Plan(Win32Interop.Rect work, IReadOnlyList<Win32Interop.Rect> items, int gap = 12)
    {
        int right = work.Right - gap, top = work.Top + gap, column = 0; List<Win32Interop.Rect> positions = [];
        foreach (var item in items)
        {
            int width = item.Right - item.Left, height = item.Bottom - item.Top;
            if (width <= 0 || height <= 0 || width > work.Right - work.Left - gap * 2 || height > work.Bottom - work.Top - gap * 2) throw new InvalidOperationException("有组件大于当前屏幕工作区，未改变任何位置。可先缩小组件。");
            if (top + height > work.Bottom - gap) { right -= column + gap; top = work.Top + gap; column = 0; }
            int left = right - width;
            if (left < work.Left + gap) throw new InvalidOperationException("当前屏幕放不下全部组件，未改变任何位置。可先使用“全部缩到最小”。");
            positions.Add(new Win32Interop.Rect { Left = left, Top = top, Right = right, Bottom = top + height });
            column = Math.Max(column, width); top += height + gap;
        }
        return positions;
    }
    internal static void Arrange(IReadOnlyList<Window> windows)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        var work = new Win32Interop.Rect { Left = screen.Left, Top = screen.Top, Right = screen.Right, Bottom = screen.Bottom };
        var handles = windows.Select(w => new WindowInteropHelper(w).Handle).ToArray();
        var original = handles.Select(h => Win32Interop.GetWindowRect(h, out var rect) ? rect : throw new InvalidOperationException("无法获取组件位置。")).ToArray();
        var positions = Plan(work, original);
        for (int i = 0; i < handles.Length; i++)
            if (!Win32Interop.SetWindowPos(handles[i], 0, positions[i].Left, positions[i].Top, 0, 0, 0x15))
            {
                for (int restore = 0; restore <= i; restore++) Win32Interop.SetWindowPos(handles[restore], 0, original[restore].Left, original[restore].Top, 0, 0, 0x15);
                throw new InvalidOperationException("排列失败，已尝试恢复原位置。");
            }
    }
}
