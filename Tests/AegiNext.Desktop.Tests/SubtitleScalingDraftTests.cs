using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleScalingDraftTests
{
    [Fact]
    public void DefaultsUseFullHdAndBalancedMatchingWithoutDirtyingTheDraft()
    {
        var draft = new SubtitleScalingDraft();

        Assert.Equal("1920", draft.ReferenceWidth.RawText);
        Assert.Equal("1080", draft.ReferenceHeight.RawText);
        Assert.Equal(0.5, draft.MatchWidthOrHeight);
        Assert.Equal(new SubtitleScalingSettings(), draft.CreateSettings());
        Assert.Null(draft.Validate());
        Assert.False(draft.IsDirty);
    }

    [Theory]
    [InlineData("", "ReferenceWidthInput")]
    [InlineData("7e-", "ReferenceWidthInput")]
    [InlineData("0", "ReferenceWidthInput")]
    [InlineData("32769", "ReferenceWidthInput")]
    [InlineData("1920.5", "ReferenceWidthInput")]
    [InlineData("-1", "ReferenceHeightInput")]
    [InlineData("1080.5", "ReferenceHeightInput")]
    public void InvalidDimensionsRetainTheRawDraftAndRejectSettings(string text, string fieldKey)
    {
        var draft = new SubtitleScalingDraft();
        var field = fieldKey == "ReferenceWidthInput" ? draft.ReferenceWidth : draft.ReferenceHeight;

        field.RawText = text;

        Assert.Equal(fieldKey, draft.Validate());
        Assert.Equal(text, field.RawText);
        Assert.True(draft.IsDirty);
        Assert.Throws<InvalidDataException>(() => draft.CreateSettings());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void LoadingPreservesPreciseMatchingAndDoesNotRaiseUserChanges(double match)
    {
        var draft = new SubtitleScalingDraft();
        var source = new SubtitleScalingSettings
        {
            ReferenceWidth = 1080, ReferenceHeight = 1920, MatchWidthOrHeight = match
        };
        var changes = 0;
        draft.Changed += (_, _) => changes++;

        draft.Load(source, CultureInfo.InvariantCulture);

        Assert.Equal(source, draft.CreateSettings());
        Assert.Equal(0, changes);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void EscapeRestoresOnlyOneDimensionAndKeepsTheOtherPendingFields()
    {
        var draft = new SubtitleScalingDraft();
        draft.ReferenceWidth.RawText = "7e-";
        draft.ReferenceHeight.RawText = "720";
        draft.MatchWidthOrHeight = 0.75;

        Assert.True(draft.RestoreField("ReferenceWidthInput"));

        Assert.Equal("1920", draft.ReferenceWidth.RawText);
        Assert.Equal("720", draft.ReferenceHeight.RawText);
        Assert.Equal(0.75, draft.MatchWidthOrHeight);
        Assert.Null(draft.Validate());
        Assert.True(draft.IsDirty);
    }

    [Fact]
    public void LoadingOneFieldUpdatesItsRestoreSnapshotWithoutReplacingOtherRawInput()
    {
        var draft = new SubtitleScalingDraft();
        draft.ReferenceHeight.RawText = "7e-";
        draft.MatchWidthOrHeight = 0.75;

        Assert.True(draft.LoadField("ReferenceWidthInput", 1280, CultureInfo.InvariantCulture));
        draft.ReferenceWidth.RawText = "800";
        Assert.True(draft.RestoreField("ReferenceWidthInput"));

        Assert.Equal("1280", draft.ReferenceWidth.RawText);
        Assert.Equal("7e-", draft.ReferenceHeight.RawText);
        Assert.Equal(0.75, draft.MatchWidthOrHeight);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void InvalidMatchingIsReportedByItsStableFieldKey(double match)
    {
        var draft = new SubtitleScalingDraft { MatchWidthOrHeight = match };

        Assert.Equal("MatchWidthOrHeightInput", draft.Validate());
        Assert.Throws<InvalidDataException>(() => draft.CreateSettings());
    }
}
