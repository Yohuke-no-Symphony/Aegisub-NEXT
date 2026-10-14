using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.EffectLibrary.Styles;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitlePositionDraftTests
{
    [Fact]
    public void RestoringOnePositionFieldPreservesOtherDraftsAndSourcePrecision()
    {
        var position = new SubtitlePosition
        {
            Anchor = new(0.25, 0.5), Pivot = new(0.5, 0.75), Offset = new(0.12500000000000003, -40)
        };
        var draft = new SubtitlePositionDraft();
        draft.Load(new() { Position = position });
        draft.OffsetX.RawText = "7e-";
        draft.OffsetY.RawText = "17";

        Assert.True(draft.RestoreField("OffsetXInput"));
        Assert.Null(draft.Validate());
        Assert.Equal("17", draft.OffsetY.RawText);
        Assert.Equal(position.Offset.X, draft.CreatePosition()!.Offset.X);
        Assert.Equal(17, draft.CreatePosition()!.Offset.Y);
        Assert.False(draft.RestoreField("MissingInput"));
    }

    [Fact]
    public void IncompleteRawTextKeepsTheControlProjectionAndExplicitEmptyClearsIt()
    {
        var field = new NumericValueDraft();
        field.Load(24);
        field.RawText = "7e-";
        field.Value = null;

        Assert.Equal("7e-", field.RawText);
        Assert.Equal(24, field.Value);
        Assert.Null(field.Parse());

        field.RawText = string.Empty;
        Assert.Null(field.Value);
        Assert.Null(field.Parse());
        field.RawText = "17";
        Assert.Equal(17, field.Value);
        Assert.Equal(17, field.Parse());
    }

    [Fact]
    public void IncompletePositionTextSurvivesLanguageRefreshAndPreventsTemplateSave()
    {
        var model = new StyleLibraryViewModel();
        model.UpdateStyles([new(Guid.NewGuid(), "position", new() { Position = new() })]);
        var submitted = 0;
        model.UpsertRequested += (_, _) => submitted++;
        model.Position.OffsetX.RawText = "7e-";

        model.SaveCommand.Execute(null);
        model.RefreshLanguage();

        Assert.Equal(0, submitted);
        Assert.Equal("7e-", model.Position.OffsetX.RawText);
        Assert.Equal("OffsetXInput", model.InvalidFieldKey);
        model.Position.OffsetX.RawText = "17";
        model.SaveCommand.Execute(null);
        Assert.Equal(1, submitted);
        Assert.Equal(17, model.Draft!.Style.Position!.Offset.X);
    }

    [Fact]
    public void LegacyAutomaticPositionCanConvertFromMeasuredBoundsWithoutLosingPrecision()
    {
        var measured = new SubtitlePosition
        {
            Anchor = new(0.5, 1), Pivot = new(0.5, 1), Offset = new(0.12500000000000003, -38.5)
        };
        var draft = new SubtitlePositionDraft();
        draft.Load(new(), measured);

        Assert.Null(draft.CreatePosition());
        draft.IsExplicit = true;
        Assert.Equal(measured, draft.CreatePosition());
        draft.AnchorX.RawText = "1.01";
        Assert.Equal("AnchorXInput", draft.Validate());
        Assert.Throws<InvalidDataException>(() => draft.CreatePosition());
    }

    [Fact]
    public void UnavailableCustomPositionTargetsTheModeSelectorAndAutomaticModeIgnoresHiddenDrafts()
    {
        var draft = new SubtitlePositionDraft();
        draft.Load(new() { Position = new() }, canCustomize: false);
        draft.OffsetX.RawText = "7e-";

        Assert.Equal("CustomPositionMode", draft.Validate());
        Assert.Throws<InvalidDataException>(() => draft.CreatePosition());

        draft.IsExplicit = false;

        Assert.Null(draft.Validate());
        Assert.Null(draft.CreatePosition());
        Assert.Equal("7e-", draft.OffsetX.RawText);
    }
}
