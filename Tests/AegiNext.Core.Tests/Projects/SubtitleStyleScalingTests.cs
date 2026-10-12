using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

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
    [InlineData(AnimationProperty.FONT_SIZE)]
    [InlineData(AnimationProperty.LETTER_SPACING)]
    [InlineData(AnimationProperty.STROKE_WIDTH)]
    [InlineData(AnimationProperty.FILL_BLUR)]
    [InlineData(AnimationProperty.STROKE_BLUR)]
    [InlineData(AnimationProperty.SHADOW_BLUR)]
    public void DimensionTracksScaleTargetsButPreserveTimingAndMultipliers(AnimationProperty property)
    {
        var rangeId = Guid.NewGuid();
        var target = new AnimationTrackTarget(property, TextRangeId: rangeId, State: SubtitleAnimationState.ACTIVE);
        var multiply = new AnimationTransformOperation(Guid.NewGuid(), new(1), new(2), 1.5)
        {
            Mode = AnimationTransformMode.MULTIPLY_BY, ComponentMask = 1
        };
        var ordered = new AnimationTrack(target, [])
        {
            InitialValue = 10,
            Transforms = [new(Guid.NewGuid(), new(0), new(1), 20, 2), multiply]
        };
        var keyed = new AnimationTrack(target, [new(MediaTime.Zero, 10), new(new(2), 20, KeyframeInterpolation.EASE_IN)]);

        var scaled = SubtitleStyleScaling.ScaleDimensions(ordered, 2);
        var scaledKeys = SubtitleStyleScaling.ScaleDimensions(keyed, 2);

        Assert.Equal(target, scaled.Target);
        Assert.Equal(20, scaled.InitialValue!.Value.Scalar);
        Assert.Equal(40, scaled.Transforms[0].Value.Scalar);
        Assert.Equal(ordered.Transforms[0].Id, scaled.Transforms[0].Id);
        Assert.Equal(ordered.Transforms[0].End, scaled.Transforms[0].End);
        Assert.Equal(ordered.Transforms[0].Acceleration, scaled.Transforms[0].Acceleration);
        Assert.Same(multiply, scaled.Transforms[1]);
        Assert.Equal(20, scaledKeys.Keyframes[0].Value.Scalar);
        Assert.Equal(40, scaledKeys.Keyframes[1].Value.Scalar);
        Assert.Equal(keyed.Keyframes[1].Interpolation, scaledKeys.Keyframes[1].Interpolation);
        Assert.Equal(10, ordered.InitialValue!.Value.Scalar);
    }

    [Fact]
    public void ShadowVectorsScaleButMotionAndMasksKeepEngineeringUnits()
    {
        var shadow = new AnimationTrack(AnimationProperty.SHADOW_OFFSET, [new(new(0), new ScenePoint(-3, 5))]);
        var position = new AnimationTrack(AnimationProperty.POSITION, [new(new(0), new ScenePoint(20, 30))]);
        var mask = new AnimationTrack(AnimationProperty.MASK_POSITION, [new(new(0), new ScenePoint(7, 9))]);
        var blur = new AnimationTrack(AnimationProperty.BLUR, [new(new(0), 4)]);

        var tracks = SubtitleStyleScaling.ScaleDimensions([shadow, position, mask, blur], 2);

        Assert.Equal(new ScenePoint(-6, 10), tracks[0].Keyframes[0].Value.Vector);
        Assert.Same(position, tracks[1]);
        Assert.Same(mask, tracks[2]);
        Assert.Same(blur, tracks[3]);
    }

    [Fact]
    public void PositionMeasurementsReturnToReferenceUnits()
    {
        var pixels = new SubtitlePosition { Anchor = new(0.25, 1), Pivot = new(0, 1), Offset = new(40, -60) };

        var stored = SubtitleStyleScaling.ToReferencePosition(pixels, new(), 3840, 2160);

        Assert.Equal(new ScenePoint(20, -30), stored.Offset);
        Assert.Equal(pixels.Anchor, stored.Anchor);
        Assert.Equal(pixels.Pivot, stored.Pivot);
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
