using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Colors;
using AegiNext.Desktop.EffectLibrary.Styles;

namespace AegiNext.Desktop.Tests;

public sealed class SettingsColorDraftTests
{
    [Fact]
    public void InvalidAccentSurvivesOtherPreferencesAndOnlyValidCommitChangesTheAccent()
    {
        var model = new ColorsSettingsViewModel(new());
        var changes = new List<string>();
        model.Changed += (_, args) => changes.Add(args.AccentColor);
        model.AccentDraft.HexText = "#bad";
        Assert.False(model.AccentDraft.TryCommit());
        Assert.Empty(changes);
        model.UpdatePreferences(new() { Theme = WorkbenchTheme.DARK });
        model.RefreshLanguage();
        Assert.Equal("#bad", model.AccentDraft.HexText);
        Assert.NotNull(model.AccentDraft.Error);
        model.AccentDraft.HexText = "#C5488580";
        Assert.True(model.AccentDraft.TryCommit());
        Assert.Equal("#C54885", Assert.Single(changes));
        Assert.Equal("#C54885", model.AccentColor);
        Assert.False(model.AccentDraft.IsDirty);
        Assert.Equal(1, model.AccentDraft.Value.Alpha);
    }

    [Fact]
    public void InvalidColorBlocksAtomicSaveAndDoesNotDiscardOtherPendingColors()
    {
        var original = new SubtitleStylePreset(Guid.NewGuid(), "HDR", new()
        {
            Fill = new(2.5123456789012345, -0.1, 0.4, 0.7),
            Stroke = new(0.25, 0.5, 1.25, 0.8),
            ShadowColor = new(0.25, 0.5, -0.1, 0.6)
        });
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([original]);
        model.FillDraft.HexText = "#bad";
        model.StrokeDraft.Blue.RawText = "2.5";
        var saved = new List<SubtitleStylePreset>();
        model.UpsertRequested += (_, args) => saved.Add(args.Preset);
        model.SaveCommand.Execute(null);
        Assert.Empty(saved);
        Assert.Equal("FillPicker.Hex", model.InvalidFieldKey);
        Assert.Equal(original.Style, model.Draft!.Style);
        model.RefreshLanguage();
        Assert.Equal("#bad", model.FillDraft.HexText);
        Assert.Equal("2.5", model.StrokeDraft.Blue.RawText);
        model.FillDraft.Restore("HexText");
        model.SaveCommand.Execute(null);
        var result = Assert.Single(saved);
        Assert.Equal(original.Style.Fill, result.Style.Fill);
        Assert.Equal(original.Style.Stroke with { Blue = 2.5 }, result.Style.Stroke);
        Assert.Equal(original.Style.ShadowColor, result.Style.ShadowColor);
    }
}
