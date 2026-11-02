using System.Collections.Immutable;
using AegiNext.Desktop.Menus;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleFormatMenuTests
{
    [Fact]
    public void FormatMenuOwnsAssSrtAndLrcCommandsAndFileKeepsVideoExport()
    {
        var file = WorkbenchMenuCatalog.Groups.Single(group => group.Key == "File");
        Assert.DoesNotContain(WorkbenchCommand.IMPORT_SUBTITLES, file.Commands);
        Assert.DoesNotContain(WorkbenchCommand.EXPORT_SUBTITLES, file.Commands);
        Assert.Contains(WorkbenchCommand.EXPORT_VIDEO, file.Commands);
        var format = WorkbenchMenuCatalog.Groups.Single(group => group.Key == "Format");
        Assert.Equal(["Aegisub", "SRT", "LRC"], format.Children.Select(group => group.Key));
        Assert.Equal<WorkbenchCommand?>([WorkbenchCommand.IMPORT_ASS, WorkbenchCommand.EXPORT_ASS], format.Children[0].Commands);
        Assert.Equal<WorkbenchCommand?>([WorkbenchCommand.IMPORT_SUBTITLES, WorkbenchCommand.EXPORT_SUBTITLES], format.Children[1].Commands);
        Assert.Equal<WorkbenchCommand?>([WorkbenchCommand.IMPORT_LRC, WorkbenchCommand.EXPORT_LRC, WorkbenchCommand.EXPORT_ENHANCED_LRC], format.Children[2].Commands);
        Assert.Equal(5, (int)WorkbenchCommand.IMPORT_SUBTITLES);
        Assert.Equal(6, (int)WorkbenchCommand.EXPORT_SUBTITLES);
        Assert.Equal(31, (int)WorkbenchCommand.VIEW_LOG);
    }

    [Fact]
    public void PreviousCompleteBindingsKeepCustomAndDisabledGesturesAndAppendCommandDefaults()
    {
        var previous = ShortcutDefaults.CreateBindings().Where(binding => binding.Command <= WorkbenchCommand.VIEW_LOG)
            .Select(binding => binding.Command switch
            {
                WorkbenchCommand.IMPORT_SUBTITLES => binding with { Gesture = "CmdOrCtrl+Alt+I" },
                WorkbenchCommand.EXPORT_SUBTITLES => binding with { Gesture = string.Empty },
                _ => binding
            }).ToImmutableArray();
        var upgraded = WorkbenchPreferencesMigration.Upgrade(new() { ShortcutBindings = previous });
        upgraded.Validate();
        Assert.Equal(previous, upgraded.ShortcutBindings.Where(binding => binding.Command <= WorkbenchCommand.VIEW_LOG));
        Assert.All(upgraded.ShortcutBindings.Where(binding => binding.Command > WorkbenchCommand.VIEW_LOG), binding => Assert.Equal(
            ShortcutDefaults.CreateBindings().Single(value => value.Command == binding.Command).Gesture, binding.Gesture));
    }
}
