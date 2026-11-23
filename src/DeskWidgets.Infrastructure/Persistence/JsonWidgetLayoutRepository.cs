using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DeskWidgets.Core;

namespace DeskWidgets.Infrastructure;

public sealed class JsonWidgetLayoutRepository(string path) : IWidgetLayoutRepository
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public IReadOnlyList<WidgetState>? Load()
    {
        if (!File.Exists(path)) return null;
        var states = JsonSerializer.Deserialize<List<WidgetState>>(File.ReadAllText(path), Json) ?? throw new JsonException("配置必须是数组。");
        if (states.Any(s => s == null || !s.IsValid()) || states.Select(s => s.Id).Distinct().Count() != states.Count) throw new JsonException("挂件配置包含无效尺寸、位置或重复 ID。");
        return states;
    }

    public void Save(IEnumerable<WidgetState> states)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(states, Json));
        File.Move(temporary, path, true);
    }
}
