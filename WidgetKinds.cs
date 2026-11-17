using System;
namespace DeskWidgets;
public sealed record WidgetCity(string Name, double Lat, double Lon)
{
    public bool IsValid() => !string.IsNullOrWhiteSpace(Name) && Name.Length <= 100 && double.IsFinite(Lat) && Math.Abs(Lat) <= 90 && double.IsFinite(Lon) && Math.Abs(Lon) <= 180;
}
internal static class WidgetKinds
{
    internal static readonly (string Zone, string Name)[] Zones = [("UTC", "UTC 标准时间"), ("Asia/Shanghai", "北京"), ("America/New_York", "纽约 · 美东"), ("America/Los_Angeles", "洛杉矶 · 美西")];
    internal static string Family(string directory) => directory switch { "clock-beijing" or "clock-new-york" or "clock-los-angeles" => "world-clock", "weather" => "weather-card", _ => directory };
    internal static string ContentDirectory(string directory) => Family(directory) == "world-clock" ? "world-clock" : directory;
    internal static string DefaultZone(string directory) => directory switch { "clock-beijing" => "Asia/Shanghai", "clock-new-york" => "America/New_York", "clock-los-angeles" => "America/Los_Angeles", _ => "UTC" };
    internal static bool ValidZone(string? zone) => Array.Exists(Zones, item => item.Zone == zone);
    internal static string Context(WidgetState state)
    {
        if (Family(state.WidgetName) == "world-clock") { string zone = state.TimeZone ?? DefaultZone(state.WidgetName); return Array.Find(Zones, item => item.Zone == zone).Name ?? zone; }
        return Family(state.WidgetName) == "weather-card" ? state.City?.Name ?? "南京" : "";
    }
}
