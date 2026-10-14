using AegiNext.Core.Presets;
using AegiNext.Desktop.EffectLibrary.Styles;

namespace AegiNext.Desktop.Tests;

public sealed class SettingsStyleScalingDraftTests
{
    [Fact]
    public void ReferenceEditingPreviewsAndSavesSettingsWithoutChangingDimensionValues()
    {
        var original = new SubtitleStylePreset(Guid.NewGuid(), "Scaling", new()
        {
            FontSize = 72, StrokeWidth = 3, Margins = new(20, 40, 30), ShadowOffset = new(2, 4)
        });
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([original], original.Id);
        SubtitleStylePreset? saved = null;
        model.UpsertRequested += (_, args) => saved = args.Preset;

        model.Scaling.ReferenceWidth.RawText = "1080";
        model.Scaling.ReferenceHeight.RawText = "1920";
        model.Scaling.MatchWidthOrHeight = 1;

        Assert.True(model.IsDirty);
        Assert.True(model.TryCreatePreviewPreset(out var preview));
        var expected = original.Style with { Scaling = model.Scaling.CreateSettings() };
        Assert.Equal(expected, preview!.Style);
        Assert.Same(original, Assert.Single(model.Styles));
        model.SaveCommand.Execute(null);
        Assert.Null(model.Error);
        Assert.Equal(expected, saved!.Style);
    }

    [Theory]
    [InlineData("7e-")]
    [InlineData("1920.5")]
    [InlineData("32769")]
    public void InvalidReferenceWidthBlocksPreviewAndSaveWhileKeepingRawText(string text)
    {
        var original = new SubtitleStylePreset(Guid.NewGuid(), "Scaling", new());
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([original], original.Id);
        var requests = 0;
        model.UpsertRequested += (_, _) => requests++;

        model.Scaling.ReferenceWidth.RawText = text;

        Assert.True(model.IsDirty);
        Assert.False(model.TryCreatePreviewPreset(out _));
        model.SaveCommand.Execute(null);
        Assert.Equal(0, requests);
        Assert.Equal("ReferenceWidthInput", model.InvalidFieldKey);
        Assert.NotNull(model.Error);
        Assert.Equal(text, model.Scaling.ReferenceWidth.RawText);
        Assert.Equal(original.Style.Scaling, model.Draft!.Style.Scaling);
    }

    [Fact]
    public void RestoringAndRefreshingLanguagePreservesOtherPendingFields()
    {
        var original = new SubtitleStylePreset(Guid.NewGuid(), "Scaling", new());
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([original], original.Id);
        model.Scaling.ReferenceWidth.RawText = "7e-";
        model.Scaling.ReferenceHeight.RawText = "720";
        model.FontSizeText = "96";
        model.SaveCommand.Execute(null);

        Assert.True(model.RestoreAppearanceField("ReferenceWidthInput"));
        model.RefreshLanguage();

        Assert.Null(model.Error);
        Assert.Equal("1920", model.Scaling.ReferenceWidth.RawText);
        Assert.Equal("720", model.Scaling.ReferenceHeight.RawText);
        Assert.Equal("96", model.FontSizeText);
        Assert.True(model.TryCreatePreviewPreset(out var preview));
        Assert.Equal(720, preview!.Style.Scaling.ReferenceHeight);
        Assert.Equal(96, preview.Style.FontSize);
    }

    [Fact]
    public void LoadingCopyingAndDiscardingKeepTheScalingContractAndExactMatching()
    {
        var original = new SubtitleStylePreset(Guid.NewGuid(), "Scaling", new()
        {
            Scaling = new() { ReferenceWidth = 1080, ReferenceHeight = 1920, MatchWidthOrHeight = 0.5000000000000001 }
        });
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([original], original.Id);

        Assert.False(model.IsDirty);
        Assert.True(model.TryCreatePreviewPreset(out var preview));
        Assert.Equal(original.Style, preview!.Style);
        model.DuplicateCommand.Execute(null);
        Assert.Equal(original.Style.Scaling, model.Draft!.Style.Scaling);
        Assert.Equal(original.Style.Scaling, model.Scaling.CreateSettings());
        model.SelectedStyle = original;
        model.Scaling.MatchWidthOrHeight = 1;
        model.DiscardDraft();
        Assert.Equal(original.Style.Scaling, model.Scaling.CreateSettings());
        Assert.False(model.IsDirty);
    }
}
