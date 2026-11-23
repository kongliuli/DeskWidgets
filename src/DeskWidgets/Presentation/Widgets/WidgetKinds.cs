using System.Linq;
namespace DeskWidgets;
internal static class WidgetKinds
{
    internal static string Family(string directory) => directory switch { "clock-beijing" or "clock-new-york" or "clock-los-angeles" => "world-clock", "weather" => "weather-card", _ => directory };
    internal static string ContentDirectory(string directory) => Family(directory) == "world-clock" ? "world-clock" : directory;
    internal static string DefaultZone(string directory) => directory switch { "clock-beijing" => "Asia/Shanghai", "clock-new-york" => "America/New_York", "clock-los-angeles" => "America/Los_Angeles", _ => "UTC" };
    internal static string Context(WidgetState state)
    {
        if (Family(state.WidgetName) == "world-clock") { string zone = state.TimeZone ?? DefaultZone(state.WidgetName); return WidgetZones.Entries.FirstOrDefault(item => item.Zone == zone).Name ?? zone; }
        return Family(state.WidgetName) == "weather-card" ? state.City?.Name ?? "南京" : "";
    }
}
