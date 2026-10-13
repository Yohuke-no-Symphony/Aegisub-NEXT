using System.Collections.Immutable;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Shortcuts;

public sealed class EffectPresetsShortcutPreferencesTests
{
    [Fact]
    public void CompletePreviousBindingsGainAnUnboundPresetPanelAndPreserveUserGestures()
    {
        var previous = ShortcutDefaults.CreateBindings().Where(binding => binding.Command <= WorkbenchCommand.VIEW_DRAWING_TOOLS)
            .Select(binding => binding.Command == WorkbenchCommand.VIEW_EFFECTS ? binding with { Gesture = "F6" } : binding).ToImmutableArray();
        var upgraded = WorkbenchPreferencesMigration.Upgrade(new() { ShortcutBindings = previous });

        upgraded.Validate();
        Assert.Equal(previous.AsEnumerable(), upgraded.ShortcutBindings.Where(binding => binding.Command != WorkbenchCommand.VIEW_EFFECT_PRESETS));
        Assert.Equal(string.Empty, Assert.Single(upgraded.ShortcutBindings, binding => binding.Command == WorkbenchCommand.VIEW_EFFECT_PRESETS).Gesture);
        Assert.Same(upgraded, WorkbenchPreferencesMigration.Upgrade(upgraded));
    }

    [Fact]
    public void IncompletePreviousBindingsRemainRejected()
    {
        var value = new WorkbenchPreferences
        {
            ShortcutBindings = ShortcutDefaults.CreateBindings().Where(binding => binding.Command <= WorkbenchCommand.VIEW_DRAWING_TOOLS &&
                binding.Command != WorkbenchCommand.SAVE_PROJECT).ToImmutableArray()
        };
        Assert.Same(value, WorkbenchPreferencesMigration.Upgrade(value));
    }
}
