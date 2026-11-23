namespace DeskWidgets;

public sealed record WidgetDefinition(string Directory, string Name, double Width, double Height, double MinWidth, double MinHeight, bool DefaultOpen = false);
