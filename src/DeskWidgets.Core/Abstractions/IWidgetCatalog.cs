using System.Collections.Generic;

namespace DeskWidgets.Core;

public interface IWidgetCatalog
{
    List<WidgetDefinition> Scan();
}
