using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskWidgets;
internal sealed class SavedNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public bool Pinned { get; set; }
    public int X { get; set; } = 100;
    public int Y { get; set; } = 100;
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 180;
}
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
internal sealed class NotesService : IDisposable
{
    private readonly WidgetManager manager;
    private readonly string path = Path.Combine(AppContext.BaseDirectory, "notes-history.json");
    private readonly List<SavedNote> notes = [];
    private readonly Dictionary<string, DesktopNoteWindow> pinned = [];
    private readonly DispatcherTimer dragTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private DesktopNoteWindow? preview;
    private WidgetWindow? dragOwner;
    private SavedNote? dragging;
    private bool writable = true, sleeping;
    internal void SetSleeping(bool value) { sleeping = value; if (value) CancelDrag(); foreach (var window in pinned.Values) window.SetSleeping(value); }
    internal IReadOnlyList<DesktopNoteWindow> Windows => pinned.Values.ToArray();
    internal NotesService(WidgetManager manager)
    {
        this.manager = manager; dragTimer.Tick += (_, _) => preview?.MoveToCursor();
    }
    internal void Restore()
    {
        if (!File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new JsonException("历史文件过大，请先备份整理。");
            var saved = JsonSerializer.Deserialize<List<SavedNote>>(File.ReadAllText(path)) ?? throw new JsonException("便签记录必须是数组。");
            if (saved.Any(n => n == null || !Guid.TryParseExact(n.Id, "N", out _) || n.Text == null || n.Text.Length > 50000 || !double.IsFinite(n.Width) || !double.IsFinite(n.Height) || n.Width is < 240 or > 32768 || n.Height is < 140 or > 32768) || saved.Select(n => n.Id).Distinct().Count() != saved.Count) throw new JsonException("便签历史包含无效记录。");
            notes.AddRange(saved);
            foreach (var note in notes.Where(n => n.Pinned)) { var window = new DesktopNoteWindow(note); pinned.Add(note.Id, window); window.Show(); if (sleeping) window.SetSleeping(true); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { writable = false; App.Report(new InvalidOperationException("便签历史无法恢复，保留原文件，不会覆盖。", error)); }
    }
    internal void Handle(WidgetWindow window, JsonElement message)
    {
        try
        {
            string type = message.GetProperty("type").GetString() ?? "";
            string id = message.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            if (type == "cancelNoteDrag") { EndDrag(window, true); return; }
            if (window.State.Locked && type is not ("requestNotes" or "readNoteHistory")) throw new InvalidOperationException("请先解锁随手记。");
            switch (type)
            {
                case "requestNotes": Publish(window); break;
                case "saveNoteHistory": if (message.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) { Add(text.GetString() ?? ""); window.PostTelemetry(new { type = "noteHistorySaved" }); } break;
                case "readNoteHistory": Read(window, id); break;
                case "startNoteDrag": BeginDrag(window, id); break;
                case "endNoteDrag": EndDrag(window, false); break;
                case "closeDesktopNote": CloseDesktop(id); break;
                case "deleteNoteHistory": Delete(id); break;
            }
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { window.PostTelemetry(new { type = "notesError", message = error.Message }); }
    }
    internal void Publish(WidgetWindow? target = null)
    {
        var state = new { type = "notesState", notes = notes.OrderByDescending(n => n.CreatedAt).Select(n => new { n.Id, preview = n.Text[..Math.Min(160, n.Text.Length)], n.CreatedAt, n.Pinned }).ToArray() };
        if (target != null) target.PostTelemetry(state); else foreach (var window in manager.OpenWindows.Where(w => w.State.WidgetName == "notes")) window.PostTelemetry(state);
    }
    internal void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 50000) throw new ArgumentException("便签需包含文字，且不能超过 50000 字。");
        var note = new SavedNote { Text = text }; notes.Add(note);
        try { Save(); } catch { notes.Remove(note); throw; }
        Publish();
    }
    internal void Read(WidgetWindow target, string id) { var note = Find(id); target.PostTelemetry(new { type = "noteContent", note.Id, note.Text }); }
    private SavedNote Find(string id) => notes.Find(n => n.Id == id) ?? throw new ArgumentException("找不到这条历史便签。");
    internal void BeginDrag(WidgetWindow owner, string id)
    {
        CancelDrag(); var note = Find(id); if (note.Pinned) throw new InvalidOperationException("这条便签已在桌面，请先从随手记收回。");
        dragging = note; dragOwner = owner; preview = new DesktopNoteWindow(note); preview.Show(); preview.MoveToCursor(); dragTimer.Start();
    }
    internal void EndDrag(WidgetWindow owner, bool cancel)
    {
        if (dragOwner != owner) return;
        if (cancel) { CancelDrag(); return; }
        dragTimer.Stop(); var note = dragging; var window = preview;
        preview = null; dragging = null; dragOwner = null;
        if (note == null || window == null) return;
        window.MoveToCursor(); window.CaptureState(); note.Pinned = true;
        try { Save(); pinned.Add(note.Id, window); window.ApplyLayer(); Publish(); }
        catch { note.Pinned = false; window.Close(); throw; }
    }
    internal void CloseDesktop(string id)
    {
        var note = Find(id); bool wasPinned = note.Pinned; note.Pinned = false;
        try { Save(); } catch { note.Pinned = wasPinned; throw; }
        if (pinned.Remove(id, out var window)) window.Close(); Publish();
    }
    internal void Delete(string id)
    {
        var note = Find(id); if (note.Pinned) throw new InvalidOperationException("请先关闭桌面便签，再删除历史记录。");
        notes.Remove(note); try { Save(); } catch { notes.Add(note); throw; } Publish();
    }
    internal void Remove(WidgetWindow owner) { if (dragOwner == owner) CancelDrag(); }
    private void CancelDrag() { dragTimer.Stop(); preview?.Close(); preview = null; dragging = null; dragOwner = null; }
    internal void RefreshLayer(bool force = false) { foreach (var window in pinned.Values) window.ApplyLayer(force); }
    internal void SavePositions() { if (pinned.Count == 0) return; foreach (var window in pinned.Values) window.CaptureState(); Save(); }
    private void Save()
    {
        if (!writable) throw new IOException("原便签历史无法读取，本次不会覆盖。");
        string json = JsonSerializer.Serialize(notes);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 64 * 1024 * 1024) throw new IOException("便签历史已达 64 MB，请先清理不需要的历史记录。");
        File.WriteAllText(path + ".tmp", json);
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(path + ".tmp", path, true);
    }
    public void Dispose() { CancelDrag(); foreach (var window in pinned.Values) window.Close(); pinned.Clear(); }
}
