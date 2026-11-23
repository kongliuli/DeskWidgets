using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DeskWidgets.Core;

namespace DeskWidgets.Infrastructure;

public sealed class JsonNoteHistoryRepository(string path) : INoteHistoryRepository
{
    private const int MaxHistoryBytes = 64 * 1024 * 1024;

    public IReadOnlyList<SavedNote> Load()
    {
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > MaxHistoryBytes) throw new JsonException("历史文件过大，请先备份整理。");
        var saved = JsonSerializer.Deserialize<List<SavedNote>>(File.ReadAllText(path)) ?? throw new JsonException("便签记录必须是数组。");
        if (saved.Any(n => n == null || !Guid.TryParseExact(n.Id, "N", out _) || n.Text == null || n.Text.Length > 50000 || !double.IsFinite(n.Width) || !double.IsFinite(n.Height) || n.Width is < 240 or > 32768 || n.Height is < 140 or > 32768) || saved.Select(n => n.Id).Distinct().Count() != saved.Count) throw new JsonException("便签历史包含无效记录。");
        return saved;
    }

    public void Save(IReadOnlyList<SavedNote> notes)
    {
        string json = JsonSerializer.Serialize(notes);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxHistoryBytes) throw new IOException("便签历史已达 64 MB，请先清理不需要的历史记录。");
        File.WriteAllText(path + ".tmp", json);
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(path + ".tmp", path, true);
    }
}
