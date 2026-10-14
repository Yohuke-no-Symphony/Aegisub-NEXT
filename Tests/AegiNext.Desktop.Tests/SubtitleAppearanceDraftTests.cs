using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.EffectLibrary.Styles;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleAppearanceDraftTests
{
    [Fact]
    public void InlineDraftPreservesUnchangedFieldsAndRestoresOnlyTheInvalidAppearanceField()
    {
        var style = new SubtitleStyle { LetterSpacing = -0.123456789123456, FillBlur = 2.123456789123456, StrokeBlur = 4 };
        var draft = new SubtitleDetailsStyleDraft();
        draft.Load(style);
        Assert.False(draft.Read(style).HasOverrides);
        draft.LetterSpacingText = "-3.5";
        draft.FillBlurText = "invalid";
        draft.StrokeBlurText = "7.25";
        Assert.Throws<InvalidDataException>(() => draft.Read(style));
        Assert.Equal(nameof(SubtitleDetailsStyleDraft.FillBlurText), draft.InvalidField);
        draft.RestoreField(nameof(SubtitleDetailsStyleDraft.FillBlurText));
        var result = draft.Read(style).ApplyTo(style);
        Assert.Equal(-3.5, result.LetterSpacing);
        Assert.Equal(style.FillBlur, result.FillBlur);
        Assert.Equal(7.25, result.StrokeBlur);
        Assert.Equal(style.WrapMode, result.WrapMode);
    }

    [Fact]
    public void KaraokeDraftExposesBothBlurChannelsWithoutTypographyAndPreservesTheOtherChannel()
    {
        var style = new KaraokeHighlightStyle { PresetId = Guid.NewGuid(), PresetName = "Highlight", FillBlur = 1.2345678912345, StrokeBlur = 3 };
        var draft = new SubtitleKaraokeStyleDraft();
        draft.Load(style);
        draft.StrokeBlurText = "6.5";
        var changed = draft.Read(style);
        Assert.Equal(style.FillBlur, changed.FillBlur);
        Assert.Equal(6.5, changed.StrokeBlur);
        Assert.DoesNotContain(typeof(SubtitleKaraokeStyleDraft).GetProperties(), property =>
            property.Name is "LetterSpacingText" or "WrapMode");
        var edit = draft.ReadOverride(new() { FillBlur = style.FillBlur, StrokeBlur = style.StrokeBlur });
        Assert.Null(edit.FillBlur);
        Assert.Equal(6.5, edit.StrokeBlur);
    }

    [Fact]
    public void StyleLibraryPreviewAndSubmissionRetainSpacingBlurAndWrapAfterInvalidFieldRestore()
    {
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Appearance", new()
        {
            LetterSpacing = 2, FillBlur = 3, StrokeBlur = 4, WrapMode = SubtitleWrapMode.NATURAL
        });
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([preset]);
        model.SelectedStyle = preset;
        model.LetterSpacingText = "-5";
        model.FillBlurText = "7e-";
        model.StrokeBlurText = "8";
        model.WrapModeIndex = (int)SubtitleWrapMode.NO_WRAP;
        Assert.False(model.TryCreatePreviewPreset(out _));
        Assert.True(model.RestoreAppearanceField("FillBlurInput"));
        Assert.Equal("-5", model.LetterSpacingText);
        Assert.Equal("8", model.StrokeBlurText);
        Assert.True(model.TryCreatePreviewPreset(out var preview));
        Assert.Equal(preset.Style with { LetterSpacing = -5, StrokeBlur = 8, WrapMode = SubtitleWrapMode.NO_WRAP }, preview!.Style);
        SubtitleStylePreset? saved = null;
        model.UpsertRequested += (_, e) => saved = e.Preset;
        model.SaveCommand.Execute(null);
        Assert.NotNull(saved);
        Assert.Equal(preview.Style, saved.Style);
        model.UpdateStyles([saved]);
        Assert.Equal(-5m, model.LetterSpacing);
        Assert.Equal(3m, model.FillBlur);
        Assert.Equal(8m, model.StrokeBlur);
        Assert.Equal((int)SubtitleWrapMode.NO_WRAP, model.WrapModeIndex);
    }
}
