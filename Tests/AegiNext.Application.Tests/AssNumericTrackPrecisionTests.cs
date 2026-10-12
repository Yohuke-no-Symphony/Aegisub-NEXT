using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssNumericTrackPrecisionTests
{
    [Theory]
    [InlineData(AnimationProperty.FONT_SIZE, false)]
    [InlineData(AnimationProperty.FONT_SIZE, true)]
    [InlineData(AnimationProperty.LETTER_SPACING, false)]
    [InlineData(AnimationProperty.LETTER_SPACING, true)]
    [InlineData(AnimationProperty.STROKE_WIDTH, false)]
    [InlineData(AnimationProperty.STROKE_WIDTH, true)]
    [InlineData(AnimationProperty.FILL_BLUR, false)]
    [InlineData(AnimationProperty.FILL_BLUR, true)]
    [InlineData(AnimationProperty.STROKE_BLUR, false)]
    [InlineData(AnimationProperty.STROKE_BLUR, true)]
    [InlineData(AnimationProperty.SCALE, false)]
    [InlineData(AnimationProperty.SCALE, true)]
    [InlineData(AnimationProperty.ROTATION, false)]
    [InlineData(AnimationProperty.ROTATION, true)]
    [InlineData(AnimationProperty.SHADOW_OFFSET, false)]
    [InlineData(AnimationProperty.SHADOW_OFFSET, true)]
    public void ScopedNumericTargetsDiagnosePrecisionOfTheirActualAssAmount(AnimationProperty property, bool quantized)
    {
        var amount = property switch
        {
            AnimationProperty.FONT_SIZE => 40,
            AnimationProperty.LETTER_SPACING => 12,
            AnimationProperty.SCALE => 240,
            AnimationProperty.ROTATION => 110,
            _ => 4
        } + (quantized ? 0.123456789123 : 0.125);
        var target = NativeValue(property, amount);
        var document = Document(property, target);

        var written = AssSubtitleFormat.Write(document);
        var imported = Import(written.Text);

        var actual = SceneEvaluator.EvaluateTrack(Track(imported, property), new(2));
        var expected = NativeValue(property, AssFormatValues.Number(AssFormatValues.Number(amount)));
        AssertValue(expected, actual);
        if (quantized)
        {
            var component = property == AnimationProperty.SHADOW_OFFSET ? 1 : 0;
            Assert.NotEqual(target.GetComponent(component), actual.GetComponent(component));
        }
        Assert.Equal(quantized, written.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.NumberPrecision"));
    }

    [Fact]
    public void ExactlyRepresentableNineDecimalFontSizeTargetDoesNotReportPrecisionLoss()
    {
        const double TARGET = 40.123456789;
        var document = Document(AnimationProperty.FONT_SIZE, TARGET);

        var written = AssSubtitleFormat.Write(document);
        var imported = Import(written.Text);

        Assert.Equal(TARGET, SceneEvaluator.EvaluateScalarTrack(Track(imported, AnimationProperty.FONT_SIZE), new(2)), 12);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.NumberPrecision");
    }

    [Theory]
    [InlineData(AnimationProperty.SCALE)]
    [InlineData(AnimationProperty.SHADOW_OFFSET)]
    public void UnemittedTargetAxisDoesNotProduceNumericPrecisionDiagnostics(AnimationProperty property)
    {
        var expected = property == AnimationProperty.SCALE ? new ScenePoint(1.2, 1) : new(2.125, 0);
        var document = Document(property, new ScenePoint(expected.X, 3.123456789123), 1);

        var written = AssSubtitleFormat.Write(document);
        var imported = Import(written.Text);

        AssertValue(expected, SceneEvaluator.EvaluateTrack(Track(imported, property), new(2)));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.NumberPrecision");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrderedAccelerationReportsItsOwnNineDecimalQuantization(bool quantized)
    {
        var acceleration = quantized ? 1.123456789123 : 1.125;
        var document = Document(AnimationProperty.FONT_SIZE, 40, acceleration: acceleration);

        var written = AssSubtitleFormat.Write(document);
        var imported = Import(written.Text);

        var track = Track(imported, AnimationProperty.FONT_SIZE);
        Assert.Equal(AssFormatValues.Number(AssFormatValues.Number(acceleration)),
            Assert.Single(track.Transforms).Acceleration);
        Assert.Equal(40, SceneEvaluator.EvaluateScalarTrack(track, new(2)), 12);
        Assert.Equal(quantized, written.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.NumberPrecision"));
    }

    private static AnimationValue NativeValue(AnimationProperty property, double amount)
    {
        return property switch
        {
            AnimationProperty.STROKE_WIDTH => amount / 2,
            AnimationProperty.FILL_BLUR or AnimationProperty.STROKE_BLUR => amount * AssBlurConversion.SigmaPerUnit / 2,
            AnimationProperty.SCALE => new ScenePoint(amount / 200, 1),
            AnimationProperty.ROTATION => amount - 90,
            AnimationProperty.SHADOW_OFFSET => new ScenePoint(0, -amount / 2),
            _ => amount
        };
    }

    private static ProjectDocument Document(AnimationProperty property, AnimationValue target, int mask = 0,
        double acceleration = 1)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = new SubtitleLine
        {
            Text = "ab",
            End = new(2),
            AnimationRanges = [range],
            Style = new()
            {
                Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 },
                FontFamily = "Noto Sans",
                FontSize = 20,
                StrokeWidth = property == AnimationProperty.FILL_BLUR ? 0 : 2,
                ShadowOffset = new(0, 0),
                ShadowColor = SceneColor.Transparent,
                ShadowBlur = 0,
                WrapMode = SubtitleWrapMode.NATURAL
            }
        };
        AnimationValue initial = property switch
        {
            AnimationProperty.FONT_SIZE => 20,
            AnimationProperty.STROKE_WIDTH => 2,
            AnimationProperty.SCALE => new ScenePoint(1, 1),
            AnimationProperty.SHADOW_OFFSET => new ScenePoint(0, 0),
            _ => 0
        };
        var track = new AnimationTrack(new AnimationTrackTarget(property, TextRangeId: range.Id), [])
        {
            InitialValue = initial,
            Transforms = [new(Guid.NewGuid(), MediaTime.Zero, new(2), target, acceleration) { ComponentMask = mask }]
        };
        var document = new ProjectDocument
        {
            Width = 640,
            Height = 360,
            Subtitles = [line],
            Layers = [new()
            {
                SubtitleId = line.Id,
                End = line.End,
                Transform = new() { Scale = new(2, 2), Rotation = 90 },
                Tracks = [track]
            }]
        };
        ProjectValidator.Validate(document);
        return document;
    }

    private static ProjectDocument Import(string source)
    {
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            AssSubtitleFormat.Parse(source, 640, 360), "ASS");
        ProjectValidator.Validate(document);
        return document;
    }

    private static AnimationTrack Track(ProjectDocument document, AnimationProperty property)
    {
        return Assert.Single(document.Layers[0].Tracks, track => track.Property == property &&
            track.Target.TextRangeId is not null && track.Target.State == SubtitleAnimationState.NORMAL);
    }

    private static void AssertValue(AnimationValue expected, AnimationValue actual)
    {
        Assert.Equal(expected.ComponentCount, actual.ComponentCount);
        for (var component = 0; component < expected.ComponentCount; component++)
        {
            Assert.Equal(expected.GetComponent(component), actual.GetComponent(component), 12);
        }
    }
}
