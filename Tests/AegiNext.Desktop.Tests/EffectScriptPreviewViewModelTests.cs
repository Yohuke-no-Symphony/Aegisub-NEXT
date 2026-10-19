using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Core.Timing;
using AegiNext.Desktop.EffectLibrary.Effects;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Tests;

public sealed class EffectScriptPreviewViewModelTests
{
    [Fact]
    public void BuiltinSelectionStartsAnIndependentLoopWithTheDefaultInputs()
    {
        var model = new EffectScriptLibraryViewModel();

        Assert.True(model.IsReadOnly);
        Assert.True(model.HasPreview);
        Assert.Equal("YOSYMPH666", model.PreviewText);
        Assert.True(model.TryGetPreviewDuration(out var duration));
        Assert.Equal(new MediaTime(1), duration);
        Assert.True(model.IsPreviewPlaying);
        Assert.True(model.PreviewLoop);
        Assert.NotNull(model.SelectedPreviewStyle);
        Assert.Equal(0, model.PreviewPosition);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public void PresentationInputsAndLocalErrorsDoNotSaveValidateOrChangeTheScriptDraft()
    {
        var preset = PersonalPreset("preview-isolation");
        var model = new EffectScriptLibraryViewModel();
        model.UpdateEffects([preset], preset.Id);
        var saves = 0;
        var validations = 0;
        model.SaveRequested += (_, _) => saves++;
        model.ValidationFailed += (_, _) => validations++;

        model.PreviewText = "预览 ABC 123\n第二行";
        model.PreviewDuration.RawText = "2.5";
        model.SetPreviewError("Preview compile failed");

        Assert.Equal(new MediaTime(5, 2), GetDuration(model));
        Assert.Equal("Preview compile failed", model.PreviewError);
        Assert.Null(model.Error);
        Assert.Equal(0, model.DiagnosticLine);
        Assert.Null(model.ValidationStatus);
        Assert.Equal(0, saves);
        Assert.Equal(0, validations);
        Assert.Same(preset, model.Draft);
        Assert.Equal(preset.Source, model.Source);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public void UnsavedSourceInvalidatesFromZeroAndKeepsAPausedTransportPaused()
    {
        var preset = PersonalPreset("unsaved-preview");
        var model = new EffectScriptLibraryViewModel();
        model.UpdateEffects([preset], preset.Id);
        model.UpdatePreviewPlayback(new(3, 4), false, false);
        var revision = model.PreviewRevision;

        model.Source = "effect broken\n";

        Assert.True(model.PreviewRevision > revision);
        Assert.Equal(0, model.PreviewPosition);
        Assert.False(model.IsPreviewPlaying);
        Assert.True(model.IsDirty);
        Assert.Equal("effect broken\n", model.Draft!.Source);
        Assert.Null(model.Error);
        Assert.Equal(preset.Source, Assert.Single(model.Effects, item => !item.IsBuiltin).Preset.Source);
    }

    [Fact]
    public void MultipleSelectionHasNoSceneAndReturningToOneScriptRestartsAnAutomaticLoop()
    {
        var model = new EffectScriptLibraryViewModel();
        var first = model.Effects[0];
        var second = model.Effects[1];
        model.SetPreviewError("old preview error");
        model.SelectEffects(first.Id, [first.Id, second.Id]);

        Assert.False(model.HasPreview);
        Assert.False(model.IsPreviewPlaying);
        Assert.Null(model.PreviewError);
        Assert.False(model.PreviewPlayPauseCommand.CanExecute(null));

        model.PreviewLoop = false;
        model.SelectEffects(second.Id, [second.Id]);

        Assert.True(model.HasPreview);
        Assert.True(model.IsPreviewPlaying);
        Assert.True(model.PreviewLoop);
        Assert.Equal(0, model.PreviewPosition);
    }

    [Theory]
    [InlineData("7e-")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("86401")]
    public void InvalidDurationRetainsItsRawTextAndDoesNotProduceAnOldValidScene(string text)
    {
        var model = new EffectScriptLibraryViewModel();
        var source = model.Source;
        var validations = 0;
        model.ValidationFailed += (_, _) => validations++;

        model.PreviewDuration.RawText = text;
        model.RefreshLanguage();

        Assert.Equal(text, model.PreviewDuration.RawText);
        Assert.False(model.TryGetPreviewDuration(out _));
        Assert.False(model.HasPreview);
        Assert.NotNull(model.PreviewError);
        Assert.Null(model.Error);
        Assert.Equal(0, validations);
        Assert.Equal(source, model.Source);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public void StyleRefreshKeepsStableSelectionAndFontFamilyAndFallsBackAfterDeletion()
    {
        var model = new EffectScriptLibraryViewModel();
        var first = new SubtitleStylePreset(Guid.NewGuid(), "First", new());
        var second = new SubtitleStylePreset(Guid.NewGuid(), "Second", new() { FontSize = 80 });
        model.UpdatePreviewStyles([first, second]);
        model.SelectedPreviewStyle = second;
        model.UpdatePreviewPlayback(new(1, 2), false, false);
        var revision = model.PreviewRevision;

        model.UpdatePreviewStyles([first, second with { Name = "Renamed" }]);

        Assert.Equal(second.Id, model.SelectedPreviewStyle!.Id);
        Assert.Equal(second.Style, model.SelectedPreviewStyle.Style);
        Assert.Equal(revision, model.PreviewRevision);
        Assert.Equal(0.5, model.PreviewPosition);
        var updated = second with { Style = second.Style with { FontSize = 96 } };
        model.UpdatePreviewStyles([first, updated]);
        Assert.True(model.PreviewRevision > revision);
        Assert.Equal(0, model.PreviewPosition);
        Assert.Equal(96, model.SelectedPreviewStyle.Style.FontSize);

        model.UpdatePreviewStyles([first]);

        Assert.Equal(model.PreviewStyles[0].Id, model.SelectedPreviewStyle.Id);
        Assert.NotEqual(second.Id, model.SelectedPreviewStyle.Id);
        Assert.Null(model.SelectedPreviewStyle.Style.FontAssetId);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public void EquivalentDurationFormattingPreservesThePresentedPositionAndRevision()
    {
        var model = new EffectScriptLibraryViewModel();
        model.UpdatePreviewPlayback(new(1, 2), false, false);
        var revision = model.PreviewRevision;

        model.PreviewDuration.RawText = "1.0";

        Assert.Equal("1.0", model.PreviewDuration.RawText);
        Assert.Equal(revision, model.PreviewRevision);
        Assert.Equal(0.5, model.PreviewPosition);
        Assert.False(model.IsPreviewPlaying);
        Assert.False(model.IsPreviewCompiling);
    }

    [Fact]
    public void FontCatalogReplacementInvalidatesOnceAndPlaybackProjectionDoesNotRecompile()
    {
        var model = new EffectScriptLibraryViewModel();
        var revision = model.PreviewRevision;

        model.PreviewFonts = SystemFontCatalog.Empty;

        Assert.True(model.PreviewRevision > revision);
        var updated = model.PreviewRevision;
        model.PreviewFonts = SystemFontCatalog.Empty;
        model.UpdatePreviewPlayback(new(1, 4), true, false);
        Assert.Equal(updated, model.PreviewRevision);
        Assert.Equal(0.25, model.PreviewPosition);
        model.PreviewPlayPauseCommand.Execute(null);
        Assert.False(model.IsPreviewPlaying);
        model.PreviewPosition = 0.5;
        Assert.Equal(updated, model.PreviewRevision);
        Assert.Equal(0.5, model.PreviewPosition);
    }

    private static EffectScriptPreset PersonalPreset(string id)
    {
        return new(Guid.NewGuid(), id, BuiltinEffectScripts.Get("fade-in").Source.Replace("fade-in", id, StringComparison.Ordinal));
    }

    private static MediaTime GetDuration(EffectScriptLibraryViewModel model)
    {
        Assert.True(model.TryGetPreviewDuration(out var duration));
        return duration;
    }
}
