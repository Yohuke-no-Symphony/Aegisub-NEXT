using System.Text.Json;
using System.Collections.Immutable;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Koumori;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Koumori;

namespace AegiNext.Desktop.Tests;

public sealed class KoumoriPreferencesTests
{
    [Fact]
    public void LegacyPreferencesKeepTheModelOffAndNewValuesRoundTrip()
    {
        var original = JsonSerializer.Deserialize<WorkbenchPreferences>("{\"Version\":1}")!;
        original.Validate();
        Assert.False(original.Koumori.Enabled);
        Assert.False(original.Koumori.SnapEnabled);
        Assert.Equal(250, original.Koumori.MaximumSnapMilliseconds);
        var changed = original with { Koumori = new() { Enabled = true, SnapEnabled = true, ModelDirectory = "models/koumori", MaximumSnapMilliseconds = 123 } };
        Assert.NotEqual(original, changed);
        Assert.Equal(changed, JsonSerializer.Deserialize<WorkbenchPreferences>(JsonSerializer.Serialize(changed)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1001")]
    [InlineData("NaN")]
    [InlineData("")]
    public void InvalidDraftSurvivesRefreshAndEscapeRestoresTheLatestSavedValue(string draft)
    {
        var model = new KoumoriSettingsViewModel(new());
        var changes = 0;
        model.Changed += (_, _) => changes++;
        model.MaximumSnapMillisecondsText = draft;
        Assert.False(model.CommitMaximumSnapMilliseconds());
        model.UpdatePreferences(new() { Koumori = new() { MaximumSnapMilliseconds = 180 } });
        model.RefreshLanguage();
        Assert.Equal(draft, model.MaximumSnapMillisecondsText);
        Assert.NotNull(model.Error);
        Assert.Equal(0, changes);
        model.RestoreMaximumSnapMilliseconds();
        Assert.Equal("180", model.MaximumSnapMillisecondsText);
        Assert.Null(model.Error);
    }

    [Fact]
    public void AConfirmedDistanceEmitsOnlyOneChange()
    {
        var model = new KoumoriSettingsViewModel(new());
        var changes = new List<KoumoriPreferences>();
        model.Changed += (_, args) => changes.Add(args.Preferences);
        model.MaximumSnapMillisecondsText = "125";
        Assert.True(model.CommitMaximumSnapMilliseconds());
        Assert.True(model.CommitMaximumSnapMilliseconds());
        Assert.Equal(125, Assert.Single(changes).MaximumSnapMilliseconds);
    }

    [Fact]
    public void BundledSourceIgnoresTheRememberedManualDirectory()
    {
        var preferences = new KoumoriPreferences { ModelDirectory = "remembered-model" };
        Assert.Equal(KoumoriModelSource.BUNDLED, preferences.ModelSource);
        Assert.Equal(KoumoriAnalyzer.DefaultModelDirectory, preferences.ResolvedModelDirectory);
        Assert.Equal("remembered-model", (preferences with { ModelSource = KoumoriModelSource.MANUAL }).ResolvedModelDirectory);
        Assert.DoesNotContain(nameof(KoumoriPreferences.ResolvedModelDirectory), JsonSerializer.Serialize(preferences));
        Assert.Throws<InvalidDataException>(() => (preferences with { ModelSource = (KoumoriModelSource)99 }).Validate());
    }

    [Fact]
    public void SwitchingSourcesPreservesManualDraftUntilExplicitlyApplied()
    {
        var preferences = new WorkbenchPreferences
        {
            Koumori = new() { ModelDirectory = "saved-model", ModelSource = KoumoriModelSource.MANUAL }
        };
        var model = new KoumoriSettingsViewModel(preferences);
        var changes = new List<KoumoriPreferences>();
        model.Changed += (_, args) => changes.Add(args.Preferences);
        var bundled = model.ModelSources.Single(choice => choice.Source == KoumoriModelSource.BUNDLED);
        var manual = model.ModelSources.Single(choice => choice.Source == KoumoriModelSource.MANUAL);
        model.ModelDirectoryText = "  new-model  ";
        model.SelectedModelSource = bundled;
        Assert.False(model.IsManualModel);
        Assert.Equal("saved-model", Assert.Single(changes).ModelDirectory);
        model.UpdatePreferences(preferences with { Koumori = changes[^1] });
        model.RefreshLanguage();
        Assert.Equal("  new-model  ", model.ModelDirectoryText);
        Assert.Same(bundled, model.SelectedModelSource);
        Assert.Single(changes);

        model.SelectedModelSource = manual;
        Assert.True(model.IsManualModel);
        Assert.Equal("saved-model", changes[^1].ModelDirectory);
        model.ApplyModelDirectoryCommand.Execute(null);
        Assert.Equal("new-model", changes[^1].ModelDirectory);
        Assert.Equal("new-model", model.ModelDirectoryText);
        Assert.Equal(KoumoriModelSource.MANUAL, changes[^1].ModelSource);
        Assert.Equal(3, changes.Count);
        model.ApplyModelDirectoryCommand.Execute(null);
        Assert.Equal(3, changes.Count);
    }

    [Fact]
    public void EmptyManualDirectoryDoesNotApplyOrFallBackToBundledModel()
    {
        var model = new KoumoriSettingsViewModel(new()
        {
            Koumori = new() { ModelDirectory = "saved-model", ModelSource = KoumoriModelSource.MANUAL }
        });
        var changes = new List<KoumoriPreferences>();
        model.Changed += (_, args) => changes.Add(args.Preferences);
        model.ModelDirectoryText = "  ";
        model.ApplyModelDirectoryCommand.Execute(null);
        Assert.Empty(changes);
        Assert.NotNull(model.Error);
        Assert.Equal("  ", model.ModelDirectoryText);
        model.SelectedModelSource = model.ModelSources.Single(choice => choice.Source == KoumoriModelSource.BUNDLED);
        Assert.Null(model.Error);
        Assert.False(model.ApplyModelDirectoryCommand.CanExecute(null));
        Assert.Equal(KoumoriAnalyzer.DefaultModelDirectory, changes[^1].ResolvedModelDirectory);
    }

    [Fact]
    public void ReplacingAnUntouchedDirectoryWithSavedPreferencesClearsItsPreviousError()
    {
        var model = new KoumoriSettingsViewModel(new()
        {
            Koumori = new() { ModelSource = KoumoriModelSource.MANUAL }
        });
        model.ApplyModelDirectoryCommand.Execute(null);
        Assert.NotNull(model.Error);

        model.UpdatePreferences(new()
        {
            Koumori = new() { ModelSource = KoumoriModelSource.MANUAL, ModelDirectory = "restored-model" }
        });

        Assert.Equal("restored-model", model.ModelDirectoryText);
        Assert.Null(model.Error);
    }

    [Fact]
    public void PreviousCompleteCommandSetAddsAutomaticTimingAndPreservesCustomAndDisabledBindings()
    {
        var previous = ShortcutDefaults.CreateBindings().Where(binding => binding.Command < WorkbenchCommand.AUTO_TIMING)
            .Select(binding => binding.Command switch
            {
                WorkbenchCommand.OPEN_PROJECT => binding with { Gesture = "CmdOrCtrl+Alt+O" },
                WorkbenchCommand.SAVE_PROJECT => binding with { Gesture = string.Empty },
                _ => binding
            }).ToImmutableArray();
        var upgraded = WorkbenchPreferencesMigration.Upgrade(new() { ShortcutBindings = previous });
        upgraded.Validate();
        Assert.Equal(previous, upgraded.ShortcutBindings.Where(binding => binding.Command < WorkbenchCommand.AUTO_TIMING));
        Assert.Equal(string.Empty, Assert.Single(upgraded.ShortcutBindings, binding => binding.Command == WorkbenchCommand.AUTO_TIMING).Gesture);
        Assert.Equal(Enum.GetValues<WorkbenchCommand>().Length, upgraded.ShortcutBindings.Length);
        Assert.Same(upgraded, WorkbenchPreferencesMigration.Upgrade(upgraded));
    }
}
