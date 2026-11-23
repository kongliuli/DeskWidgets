using System;

namespace DeskWidgets;

public sealed class SavedNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public bool Pinned { get; set; }
    public int X { get; set; } = 100;
    public int Y { get; set; } = 100;
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 180;
}
