using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DeskWidgets.Core;

namespace DeskWidgets.Infrastructure;

public sealed class FileWidgetCatalog(string widgetsRoot) : IWidgetCatalog
{
    public List<WidgetDefinition> Scan()
    {
        List<WidgetDefinition> definitions = [];
        foreach (var folder in Directory.EnumerateDirectories(widgetsRoot))
        {
            try
            {
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0 || !File.Exists(Path.Combine(folder, "index.html"))) continue;
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "widget.json")));
                var root = json.RootElement;
                double Number(string name, double fallback) => root.TryGetProperty(name, out var value) && value.TryGetDouble(out var n) && double.IsFinite(n) && n is >= 1 and <= 32768 ? n : fallback;
                definitions.Add(new(Path.GetFileName(folder), root.GetProperty("name").GetString() ?? Path.GetFileName(folder), Number("defaultWidth", Number("width", 320)), Number("defaultHeight", Number("height", 190)), Number("minWidth", 240), Number("minHeight", 140), root.TryGetProperty("defaultOpen", out var open) && open.ValueKind == JsonValueKind.True));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException) { System.Diagnostics.Trace.WriteLine(error); }
        }
        return definitions;
    }
}
