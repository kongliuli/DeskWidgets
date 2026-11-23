using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

using DeskWidgets.Core;

namespace DeskWidgets;
internal sealed class NotesService : IDisposable
{
    private readonly WidgetManager manager;
    private readonly INoteHistoryRepository historyRepository;
    private readonly List<SavedNote> notes = [];
    private readonly Dictionary<string, DesktopNoteWindow> pinned = [];
    private readonly DispatcherTimer dragTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private DesktopNoteWindow? preview;
    private WidgetWindow? dragOwner;
    private SavedNote? dragging;
    private bool writable = true, sleeping;
    internal void SetSleeping(bool value) { sleeping = value; if (value) CancelDrag(); foreach (var window in pinned.Values) window.SetSleeping(value); }
    internal IReadOnlyList<DesktopNoteWindow> Windows => pinned.Values.ToArray();
    internal NotesService(WidgetManager manager, INoteHistoryRepository historyRepository)
    {
        this.manager = manager;
        this.historyRepository = historyRepository;
        dragTimer.Tick += (_, _) => preview?.MoveToCursor();
    }
    internal void Restore()
    {
        try
        {
            notes.AddRange(historyRepository.Load());
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
        historyRepository.Save(notes);
    }
    public void Dispose() { CancelDrag(); foreach (var window in pinned.Values) window.Close(); pinned.Clear(); }
}
