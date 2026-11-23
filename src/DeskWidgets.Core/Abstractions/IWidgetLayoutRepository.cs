using System.Collections.Generic;

namespace DeskWidgets.Core;

public interface IWidgetLayoutRepository
{
    IReadOnlyList<WidgetState>? Load();
    void Save(IEnumerable<WidgetState> states);
}
