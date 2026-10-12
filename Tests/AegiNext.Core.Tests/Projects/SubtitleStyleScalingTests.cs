using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class SubtitleStyleScalingTests
{
    [Theory]
    [InlineData(1280, 720, 0, 2.0 / 3)]
    [InlineData(1280, 720, 1, 2.0 / 3)]
    [InlineData(3840, 2160, 0.5, 2)]
    [InlineData(1080, 1920, 0, 0.5625)]
    [InlineData(1080, 1920, 0.5, 1)]
    [InlineData(1080, 1920, 1, 16.0 / 9)]
    public void MatchesWidthAndHeightInLogarithmicSpace(int width, int height, double match, double expected)
    {
        var settings = new SubtitleScalingSettings { MatchWidthOrHeight = match };

        Assert.Equal(expected, SubtitleStyleScaling.GetScaleFactor(settings, width, height), 12);
    }

    [Fact]
    public void CanvasProjectionScalesDimensionsWithoutChangingSourceOrDimensionlessValues()
    {
        var source = new SubtitleStyle
        {
            FontSize = 50, LetterSpacing = -3, FillBlur = 1, StrokeWidth = 4, StrokeBlur = 2,
            Margins = new(20, 30, 40), ShadowOffset = new(-2, 3), ShadowBlur = 5,
            Position = new() { Anchor = new(0.25, 0.75), Pivot = new(0, 1), Offset = new(10, -15) },
            LineHeight = 1.5, Fill = new(2, 0.5, 0.25, 0.6), Bold = true
        };

        var projected = SubtitleStyleScaling.ToCanvasUnits(source, 3840, 2160);

        Assert.Equal(100, projected.FontSize);
        Assert.Equal(-6, projected.LetterSpacing);
        Assert.Equal(2, projected.FillBlur);
        Assert.Equal(8, projected.StrokeWidth);
        Assert.Equal(4, projected.StrokeBlur);
        Assert.Equal(new SubtitleMargins(40, 60, 80), projected.Margins);
        Assert.Equal(new ScenePoint(-4, 6), projected.ShadowOffset);
        Assert.Equal(10, projected.ShadowBlur);
        Assert.Equal(new ScenePoint(20, -30), projected.Position!.Offset);
        Assert.Equal(source.Position!.Anchor, projected.Position.Anchor);
        Assert.Equal(source.Position.Pivot, projected.Position.Pivot);
        Assert.Equal(source.Fill, projected.Fill);
        Assert.Equal(source.LineHeight, projected.LineHeight);
        Assert.Equal(source.Bold, projected.Bold);
        Assert.Equal(50, source.FontSize);
        Assert.Equal(1920, source.Scaling.ReferenceWidth);
        Assert.Equal(projected, SubtitleStyleScaling.ToCanvasUnits(projected, 3840, 2160));
    }

    [Fact]
    public void ConvertsEveryTextAndKaraokeOverrideToTheDestinationReferenceUnits()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2) { Offset = new(3, 4) };
        var line = new SubtitleLine
        {
            Text = "AB", Style = new() { FontSize = 60, StrokeWidth = 2 },
            InlineSpans = [new(0, 1, new() { FontSize = 90, LetterSpacing = 0, StrokeWidth = 0 })],
            KaraokeStyle = new() { StrokeWidth = 4, ShadowOffset = new(3, 2) },
            KaraokeStyleSpans = [new(0, 2, new() { StrokeBlur = 6 }, new() { ShadowBlur = 0 })],
            AnimationRanges = [range]
        };
        var target = new SubtitleScalingSettings { ReferenceWidth = 1280, ReferenceHeight = 720 };

        var converted = SubtitleStyleScaling.ConvertUnits(line, target, 3840, 2160);

        Assert.Equal(40, converted.Style.FontSize, 10);
        Assert.Equal(60, converted.InlineSpans[0].Style.FontSize!.Value, 10);
        Assert.Equal(0, converted.InlineSpans[0].Style.StrokeWidth);
        Assert.Null(converted.InlineSpans[0].Style.ShadowBlur);
        Assert.Equal(8.0 / 3, converted.KaraokeStyle!.StrokeWidth, 10);
        Assert.Equal(2, converted.KaraokeStyle.ShadowOffset.X, 10);
        Assert.Equal(4.0 / 3, converted.KaraokeStyle.ShadowOffset.Y, 10);
        Assert.Equal(4, converted.KaraokeStyleSpans[0].ActiveStyle!.StrokeBlur!.Value, 10);
        Assert.Equal(0, converted.KaraokeStyleSpans[0].InactiveStyle!.ShadowBlur);
        Assert.Same(range, converted.AnimationRanges[0]);
        Assert.Equal(line.Id, converted.Id);
        Assert.Equal(line.Text, converted.Text);
        Assert.Equal(target, converted.Style.Scaling);
    }

    [Fact]
    public void IdentityConversionPreservesTheOriginalSnapshot()
    {
        var line = new SubtitleLine();

        Assert.Same(line, SubtitleStyleScaling.ConvertUnits(line, line.Style.Scaling, 1280, 720));
        Assert.Same(line.Style, SubtitleStyleScaling.ScaleDimensions(line.Style, 1));
    }

    [Theory]
    [InlineData(0, 1080, 0.5)]
    [InlineData(1920, 32769, 0.5)]
    [InlineData(1920, 1080, -0.01)]
    [InlineData(1920, 1080, 1.01)]
    [InlineData(1920, 1080, double.NaN)]
    [InlineData(1920, 1080, double.PositiveInfinity)]
    public void RejectsInvalidReferenceSettings(int width, int height, double match)
    {
        var style = new SubtitleStyle
        {
            Scaling = new() { ReferenceWidth = width, ReferenceHeight = height, MatchWidthOrHeight = match }
        };

        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleStyle(style));
    }

    [Fact]
    public void RejectsNullReferenceSettingsAndInvalidConversionFactors()
    {
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleStyle(new() { Scaling = null! }));
        Assert.Throws<ArgumentOutOfRangeException>(() => SubtitleStyleScaling.ScaleDimensions(new SubtitleStyle(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SubtitleStyleScaling.ScaleDimensions(new SubtitleStyle(), double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => SubtitleStyleScaling.GetScaleFactor(new(), 0, 1080));
    }
}
