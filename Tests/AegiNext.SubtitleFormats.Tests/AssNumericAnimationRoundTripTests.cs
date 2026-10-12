using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssNumericAnimationRoundTripTests
{
    [Theory]
    [InlineData(AnimationProperty.LETTER_SPACING, 1)]
    [InlineData(AnimationProperty.LETTER_SPACING, 2)]
    [InlineData(AnimationProperty.STROKE_WIDTH, 1)]
    [InlineData(AnimationProperty.STROKE_WIDTH, 2)]
    [InlineData(AnimationProperty.FILL_BLUR, 1)]
    [InlineData(AnimationProperty.FILL_BLUR, 2)]
    [InlineData(AnimationProperty.STROKE_BLUR, 1)]
    [InlineData(AnimationProperty.STROKE_BLUR, 2)]
    [InlineData(AnimationProperty.ROTATION, 1)]
    [InlineData(AnimationProperty.ROTATION, 2)]
    [InlineData(AnimationProperty.SCALE, 1)]
    [InlineData(AnimationProperty.SCALE, 2)]
    public void RepresentableCurvesPreserveEvaluatedValuesAcrossInlineResetsAndBothClockOffsets(AnimationProperty property, double exponent)
    {
        var line = Line(property);
        var origin = new MediaTime(5, 4);
        var track = new AnimationTrack(property,
        [
            new(origin, Value(property, 2), KeyframeInterpolation.POWER) { Exponent = exponent },
            new(origin + new MediaTime(2), Value(property, 8))
        ]);
        var layer = Layer(line) with { AnimationOffset = origin, Tracks = [track] };
        var document = Document(line, layer);

        var written = AssSubtitleFormat.Write(document, new(17, 100));
        Assert.Contains("\\t(", written.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(written.Diagnostics, item => item.Code == "Subtitle.Composition");
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips);
        Assert.Contains(imported.Tracks, candidate => candidate.Property == property);
        var importedLayer = Layer(imported.Line) with
        {
            Transform = imported.Transform, Tracks = imported.Tracks, AnimationOffset = imported.ContentOffset
        };
        foreach (var sample in new[] { new MediaTime(0), new(1, 8), new(1, 2), new(1), new(3, 2), new(199, 100) })
        {
            var before = Read(SceneEvaluator.EvaluateLayer(layer, line, origin + sample), property);
            var after = Read(SceneEvaluator.EvaluateLayer(importedLayer, imported.Line, imported.ContentOffset + sample), property);
            AssertValue(before, after);
        }
        Assert.Same(line, document.Subtitles[0]);
        Assert.Same(layer, document.Layers[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrderedTransformsKeepSourceOrderWhenAnEarlierOperationIsStillRunning(bool completedLaterOperation)
    {
        var line = Line(AnimationProperty.LETTER_SPACING);
        var origin = new MediaTime(5, 4);
        var track = new AnimationTrack(AnimationProperty.LETTER_SPACING, [])
        {
            InitialValue = 0,
            Transforms =
            [
                new(Guid.NewGuid(), new(0), new(3), 20, 2),
                new(Guid.NewGuid(), completedLaterOperation ? new(-2) : new(1), completedLaterOperation ? new(1, 2) : new(2), 4),
                new(Guid.NewGuid(), origin, origin + new MediaTime(2), 8)
            ]
        };
        var layer = Layer(line) with { AnimationOffset = origin, Tracks = [track] };
        var written = AssSubtitleFormat.Write(Document(line, layer));
        var clip = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips);
        var converted = Assert.Single(clip.Tracks, item => item.Property == track.Property);

        foreach (var elapsed in new[] { new MediaTime(0), new(1, 10), new(1, 2), new(1), new(199, 100) })
        {
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(track, origin + elapsed),
                SceneEvaluator.EvaluateScalarTrack(converted, clip.ContentOffset + elapsed), 7);
        }
        Assert.DoesNotContain(written.Diagnostics, item => item.Code == "Subtitle.Composition");
    }

    [Fact]
    public void MaskSampleEventsRetainNumericCurvePhaseAtTheirActualAssStartTimes()
    {
        var line = Line(AnimationProperty.LETTER_SPACING);
        var origin = new MediaTime(5, 4);
        var spacing = new AnimationTrack(AnimationProperty.LETTER_SPACING,
        [
            new(origin, 2, KeyframeInterpolation.POWER) { Exponent = 2 },
            new(origin + new MediaTime(2), 8)
        ]);
        var layer = Layer(line) with
        {
            AnimationOffset = origin,
            Mask = new VectorClipMask
            {
                Contours = [new() { Nodes = [new() { Position = new(0, 0) }, new() { Position = new(100, 0) }, new() { Position = new(100, 100) }] }]
            },
            Tracks =
            [
                spacing,
                new(AnimationProperty.MASK_POSITION,
                    [new(origin, new ScenePoint(0, 0)), new(origin + new MediaTime(2), new ScenePoint(30, 0))])
            ]
        };
        var offset = new MediaTime(17, 1000);
        var written = AssSubtitleFormat.Write(Document(line, layer) with { FrameRate = new(4, 1) }, offset);
        var clips = AssSubtitleFormat.Parse(written.Text, 640, 360).Clips;

        Assert.Equal(9, clips.Length);
        foreach (var clip in clips)
        {
            var track = Assert.Single(clip.Tracks, candidate => candidate.Property == AnimationProperty.LETTER_SPACING);
            var duration = clip.Line.End - clip.Line.Start;
            var elapsed = new MediaTime(duration.Numerator, duration.Denominator * 2);
            var originalClock = clip.Line.Start - offset - line.Start + origin + elapsed;
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(spacing, originalClock),
                SceneEvaluator.EvaluateScalarTrack(track, clip.ContentOffset + elapsed), 7);
        }
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.MaskAnimationExpanded");
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Subtitle.Composition");
    }

    [Fact]
    public void AZeroScaleEntranceRetainsBothAxesAndItsVisibleAnimation()
    {
        var line = Line(AnimationProperty.SCALE);
        var layer = Layer(line) with
        {
            Tracks = [new(AnimationProperty.SCALE,
                [new(new(0), AnimationValue.FromVector(new(0, 0))), new(new(2), AnimationValue.FromVector(new(2, 3)))])]
        };
        var written = AssSubtitleFormat.Write(Document(line, layer));
        var clip = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips);
        var track = Assert.Single(clip.Tracks, item => item.Property == AnimationProperty.SCALE);

        Assert.Equal(new ScenePoint(0, 0), SceneEvaluator.EvaluateVectorTrack(track, clip.ContentOffset));
        Assert.Equal(new ScenePoint(1, 1.5), SceneEvaluator.EvaluateVectorTrack(track, clip.ContentOffset + new MediaTime(1)));
        Assert.DoesNotContain(written.Diagnostics, item => item.Code == "Ass.TransformScale");
    }

    private static SubtitleLine Line(AnimationProperty property)
    {
        return new()
        {
            Text = "ab", Start = new(203, 100), End = new(403, 100),
            Style = new()
            {
                FontSize = 28, Alignment = TextAlignment.TOP_LEFT, WrapMode = SubtitleWrapMode.NO_WRAP,
                Position = new() { Anchor = new(0, 0), Pivot = new(0, 0), Offset = new(100, 80) },
                StrokeWidth = property == AnimationProperty.STROKE_BLUR ? 2 : 0,
                ShadowOffset = new(0, 0), ShadowBlur = 0, ShadowColor = SceneColor.Black with { Alpha = 0 }
            },
            InlineSpans = [new(1, 1, new() { Fill = SceneColor.Black })]
        };
    }

    private static ProjectLayer Layer(SubtitleLine line) => new() { SubtitleId = line.Id, Start = line.Start, End = line.End };

    private static ProjectDocument Document(SubtitleLine line, ProjectLayer layer) => new()
    {
        Width = 640, Height = 360, Subtitles = [line], Layers = [layer]
    };

    private static AnimationValue Value(AnimationProperty property, double value) => property == AnimationProperty.SCALE
        ? AnimationValue.FromVector(new(value, value + 1)) : value;

    private static AnimationValue Read(EvaluatedLayer layer, AnimationProperty property) => property switch
    {
        AnimationProperty.LETTER_SPACING => layer.LetterSpacing,
        AnimationProperty.FILL_BLUR => layer.FillBlur,
        AnimationProperty.STROKE_BLUR => layer.StrokeBlur,
        AnimationProperty.STROKE_WIDTH => layer.StrokeWidth,
        AnimationProperty.ROTATION => layer.Transform.Rotation,
        AnimationProperty.SCALE => AnimationValue.FromVector(layer.Transform.Scale),
        _ => throw new ArgumentOutOfRangeException(nameof(property))
    };

    private static void AssertValue(AnimationValue expected, AnimationValue actual)
    {
        Assert.Equal(expected.ComponentCount, actual.ComponentCount);
        for (var component = 0; component < expected.ComponentCount; component++)
        {
            Assert.Equal(expected.GetComponent(component), actual.GetComponent(component), 7);
        }
    }
}
