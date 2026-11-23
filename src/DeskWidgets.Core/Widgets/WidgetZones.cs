using System;
using System.Collections.Generic;
using System.Linq;

namespace DeskWidgets;

public static class WidgetZones
{
    public static IReadOnlyList<(string Zone, string Name)> Entries { get; } = Array.AsReadOnly<(string Zone, string Name)>([("UTC", "UTC 标准时间"), ("Asia/Shanghai", "北京"), ("America/New_York", "纽约 · 美东"), ("America/Los_Angeles", "洛杉矶 · 美西")]);

    public static bool IsValid(string? zone) => Entries.Any(item => item.Zone == zone);
}
