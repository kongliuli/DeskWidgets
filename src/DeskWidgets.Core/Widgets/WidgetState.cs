using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace DeskWidgets;

public sealed class WidgetState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WidgetName { get; set; } = "world-clock";
    public double Left { get; set; } = 64;
    public double Top { get; set; } = 64;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 190;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TimeZone { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WidgetCity? City { get; set; }
    public bool Locked { get; set; }
    public bool Topmost { get; set; }
    public bool IsValid() => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(WidgetName) && new[] { Left, Top, Width, Height }.All(double.IsFinite) && Width is >= 1 and <= 32768 && Height is >= 1 and <= 32768 && (TimeZone == null || WidgetZones.IsValid(TimeZone)) && (City == null || City.IsValid());
}
