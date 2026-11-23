using System;

namespace DeskWidgets;

public sealed record WidgetCity(string Name, double Lat, double Lon)
{
    public bool IsValid() => !string.IsNullOrWhiteSpace(Name) && Name.Length <= 100 && double.IsFinite(Lat) && Math.Abs(Lat) <= 90 && double.IsFinite(Lon) && Math.Abs(Lon) <= 180;
}
