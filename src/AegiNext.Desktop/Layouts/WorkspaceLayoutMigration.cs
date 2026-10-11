namespace AegiNext.Desktop.Layouts;

internal static class WorkspaceLayoutMigration
{
    internal static WorkspaceLayoutFile Upgrade(WorkspaceLayoutFile file)
    {
        if (file.Version is not (1 or 2 or 3 or 4 or 5 or 6))
        {
            return file;
        }
        var previousIds = WorkbenchPanelIds.All.Where(id => (file.Version >= 6 || id != WorkbenchPanelIds.DRAWING_TOOLS) &&
            (file.Version >= 5 || id != WorkbenchPanelIds.ASSETS) &&
            (file.Version >= 4 || id != WorkbenchPanelIds.MASKS) &&
            (file.Version >= 3 || id != WorkbenchPanelIds.SUBTITLE_DETAILS) &&
            (file.Version != 1 || id != WorkbenchPanelIds.LOG)).ToArray();
        WorkspaceLayoutSnapshot UpgradeSnapshot(WorkspaceLayoutSnapshot layout)
        {
            WorkspaceLayoutValidator.ValidateLegacy(layout, previousIds, file.Version);
            var additions = WorkbenchPanelIds.All.Where(id => !previousIds.Contains(id));
            return layout with { Version = WorkspaceLayoutSnapshot.CURRENT_VERSION,
                HiddenPanelIds = layout.HiddenPanelIds.Concat(additions).ToArray() };
        }
        var current = UpgradeSnapshot(file.Current);
        var presets = file.Presets.Select(preset => preset with { Layout = UpgradeSnapshot(preset.Layout) }).ToArray();
        var builtIn = WorkspaceLayoutPresets.BuiltIn.FirstOrDefault(preset => preset.Id == file.CurrentPresetId);
        if (builtIn is not null)
        {
            var legacy = WorkspaceLayoutPresetHistory.Find(builtIn.Id, file.Version, previousIds);
            if (legacy is not null && WorkspaceLayoutStore.Fingerprint(file.Current) == WorkspaceLayoutStore.Fingerprint(legacy))
            {
                current = builtIn.Layout;
            }
        }
        return file with { Version = WorkspaceLayoutSnapshot.CURRENT_VERSION, Current = current, Presets = presets };
    }
}
