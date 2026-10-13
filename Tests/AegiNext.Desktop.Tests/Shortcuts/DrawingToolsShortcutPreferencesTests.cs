using System.Collections.Immutable;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Shortcuts;

public sealed class DrawingToolsShortcutPreferencesTests
{
    [Fact]
    public void PreviousCompleteImageCommandBindingsGainTheUnboundDrawingPanelWithoutChangingUserBindings()
    {
        var previous = ShortcutDefaults.CreateBindings().Where(binding => binding.Command <= WorkbenchCommand.IMPORT_IMAGES)
            .Select(binding => binding.Command switch
            {
                WorkbenchCommand.OPEN_PROJECT => binding with { Gesture = "CmdOrCtrl+Alt+O" },
                WorkbenchCommand.SAVE_PROJECT => binding with { Gesture = string.Empty },
                WorkbenchCommand.VIEW_ASSETS => binding with { Gesture = "F6" },
                _ => binding
            }).ToImmutableArray();

        var upgraded = WorkbenchPreferencesMigration.Upgrade(new() { ShortcutBindings = previous });

        upgraded.Validate();
        Assert.Equal(previous.AsEnumerable(), upgraded.ShortcutBindings.Where(binding => binding.Command <= WorkbenchCommand.IMPORT_IMAGES));
        Assert.Equal(string.Empty, Assert.Single(upgraded.ShortcutBindings, binding => binding.Command == WorkbenchCommand.VIEW_DRAWING_TOOLS).Gesture);
        Assert.Same(upgraded, WorkbenchPreferencesMigration.Upgrade(upgraded));
    }

    [Fact]
    public void IncompleteImageCommandBindingsAreNotMistakenForACompatibleOldConfiguration()
    {
        var original = new WorkbenchPreferences
        {
            ShortcutBindings = ShortcutDefaults.CreateBindings().Where(binding => binding.Command <= WorkbenchCommand.IMPORT_IMAGES &&
                binding.Command != WorkbenchCommand.SAVE_PROJECT).ToImmutableArray()
        };
        Assert.Same(original, WorkbenchPreferencesMigration.Upgrade(original));
    }
}
