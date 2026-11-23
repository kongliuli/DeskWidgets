using System.Collections.Generic;

namespace DeskWidgets.Core;

public interface INoteHistoryRepository
{
    IReadOnlyList<SavedNote> Load();
    void Save(IReadOnlyList<SavedNote> notes);
}
