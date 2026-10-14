using System.Collections.Immutable;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Menus;

internal sealed record WorkbenchMenuGroup(string Key, ImmutableArray<WorkbenchCommand?> Commands,
    ImmutableArray<WorkbenchMenuGroup> Children = default, string? LocalizationKey = null, string? IconKey = null)
{
    internal string LabelKey => LocalizationKey ?? "Workbench." + Key;
}
