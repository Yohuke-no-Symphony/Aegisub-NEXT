using System.Collections.Immutable;
using System.Text.RegularExpressions;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssKaraokeSharedAnimationExportTests
{
    private static readonly string[] overlappingTargets = ["8", "10", "6"];
    private static readonly string[] timedTargets = ["26", "6"];

    [Theory]
    [InlineData(AnimationProperty.STROKE)]
    [InlineData(AnimationProperty.STROKE_WIDTH)]
    [InlineData(AnimationProperty.SHADOW_COLOR)]
    [InlineData(AnimationProperty.SHADOW_OFFSET)]
    [InlineData(AnimationProperty.FILL_BLUR)]
    [InlineData(AnimationProperty.STROKE_BLUR)]
    [InlineData(AnimationProperty.SHADOW_BLUR)]
    public void InactiveAnimationWinsEachSharedChannelAndKeepsItsContinuousTransform(AnimationProperty property)
    {
        var line = Line(property == AnimationProperty.FILL_BLUR ? 0 : 1);
        var inactive = Track(property, SubtitleAnimationState.INACTIVE, 2, 6);
        var active = Track(property, SubtitleAnimationState.ACTIVE, 12, 16);
        var normal = Track(property, SubtitleAnimationState.NORMAL, 22, 26);
        var document = Document(line, normal, active, inactive);

        var written = AssSubtitleFormat.Write(document);
        var body = Body(written.Text);

        Assert.Contains(Transform(property, 6), body, StringComparison.Ordinal);
        Assert.DoesNotContain(Transform(property, 16), body, StringComparison.Ordinal);
        Assert.DoesNotContain(Transform(property, 26), body, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(body, @"\\t\("));
        Assert.Contains("\\kf200", body, StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, item => item.Code == "Ass.KaraokeAnimation" && item.SubtitleId == line.Id);
        Assert.Same(inactive, document.Layers[0].Tracks[2]);
    }

    [Theory]
    [InlineData(AnimationProperty.STROKE_WIDTH)]
    [InlineData(AnimationProperty.SHADOW_OFFSET)]
    [InlineData(AnimationProperty.SHADOW_COLOR)]
    [InlineData(AnimationProperty.SHADOW_BLUR)]
    public void ActiveAnimationIsProjectedWhenTheInactiveChannelHasNoTrack(AnimationProperty property)
    {
        var line = Line();
        var written = AssSubtitleFormat.Write(Document(line,
            Track(property, SubtitleAnimationState.NORMAL, 22, 26),
            Track(property, SubtitleAnimationState.ACTIVE, 2, 6)));

        Assert.Contains(Transform(property, 6), Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain(Transform(property, 26), Body(written.Text), StringComparison.Ordinal);
        Assert.Single(Regex.Matches(Body(written.Text), @"\\t\("));
        Assert.Contains(written.Diagnostics, item => item.Code == "Ass.KaraokeAnimation");
    }

    [Fact]
    public void OverlappingRangesOverrideWithinEachStateBeforeTheSharedChannelIsChosen()
    {
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        var line = Line() with { Text = "abc", AnimationRanges = [first, second], Karaoke = [Segment(3)] };
        var written = AssSubtitleFormat.Write(Document(line,
            Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.INACTIVE, 2, 6),
            Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.INACTIVE, 3, 8, first.Id),
            Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.INACTIVE, 4, 10, second.Id),
            Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.ACTIVE, 20, 24, second.Id)));

        var targets = Regex.Matches(Body(written.Text), @"\\t\(0,2000,1,\\bord(\d+)\)")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(overlappingTargets, targets);
        Assert.Contains(written.Diagnostics, item => item.Code == "Ass.RangeOverlap");
        Assert.Same(first, line.AnimationRanges[0]);
        Assert.Same(second, line.AnimationRanges[1]);
    }

    [Fact]
    public void WholeInactiveTrackWinsOverScopedActiveTrackWithoutLeakingOutsideTheTimedRange()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        var line = Line() with
        {
            AnimationRanges = [range],
            Karaoke = [new(1, 1, new(1, 4), new(3, 4), SceneColor.White)]
        };
        var written = AssSubtitleFormat.Write(Document(line,
            Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.NORMAL, 22, 26),
            Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.INACTIVE, 2, 6),
            Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.ACTIVE, 12, 16, range.Id)));
        var targets = Regex.Matches(Body(written.Text), @"\\t\(0,2000,1,\\bord(\d+)\)")
            .Select(match => match.Groups[1].Value).ToArray();

        Assert.Equal(timedTargets, targets);
        Assert.Contains(written.Diagnostics, item => item.Code == "Ass.DormantKaraokeAnimation");
        Assert.Contains(written.Diagnostics, item => item.Code == "Ass.KaraokeAnimation");
    }

    [Fact]
    public void BlurCompetitionProducesOnlyTheVisibleEdgeTrackInThePreferredState()
    {
        var line = Line();
        var written = AssSubtitleFormat.Write(Document(line,
            Track(AnimationProperty.STROKE_BLUR, SubtitleAnimationState.NORMAL, 22, 26),
            Track(AnimationProperty.STROKE_BLUR, SubtitleAnimationState.ACTIVE, 12, 16),
            Track(AnimationProperty.STROKE_BLUR, SubtitleAnimationState.INACTIVE, 2, 6),
            Track(AnimationProperty.FILL_BLUR, SubtitleAnimationState.INACTIVE, 3, 8),
            Track(AnimationProperty.SHADOW_BLUR, SubtitleAnimationState.INACTIVE, 4, 10)));

        Assert.Contains(Transform(AnimationProperty.STROKE_BLUR, 6), Body(written.Text), StringComparison.Ordinal);
        Assert.Single(Regex.Matches(Body(written.Text), @"\\t\([^}]*?\\blur"));
        Assert.DoesNotContain(Transform(AnimationProperty.FILL_BLUR, 8), Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain(Transform(AnimationProperty.SHADOW_BLUR, 10), Body(written.Text), StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, item => item.Code == "Ass.BlurAnimation");
    }

    [Fact]
    public void SelectedZeroWidthUsesFillBlurInsteadOfTheOrdinaryStylesStrokeBlur()
    {
        var written = AssSubtitleFormat.Write(Document(Line(3),
            Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.INACTIVE, 0, 0),
            Track(AnimationProperty.FILL_BLUR, SubtitleAnimationState.INACTIVE, 2, 6),
            Track(AnimationProperty.STROKE_BLUR, SubtitleAnimationState.INACTIVE, 3, 8)));

        Assert.Contains(Transform(AnimationProperty.FILL_BLUR, 6), Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain(Transform(AnimationProperty.STROKE_BLUR, 8), Body(written.Text), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedBlurReadsAnUnconsumedNormalBorderAtTheEventOriginAndRespectsInactiveStaticWidth(bool staticOverride)
    {
        var line = Line(3) with
        {
            KaraokeStyleSpans = staticOverride ? [new(0, 2, InactiveStyle: new() { StrokeWidth = 0 })] : []
        };
        var width = new AnimationTrack(AnimationProperty.STROKE_WIDTH,
        [
            new(MediaTime.Zero, 3),
            new(new(1), staticOverride ? 3 : 0),
            new(new(2), 4)
        ]);
        var document = Document(line, width,
            Track(AnimationProperty.FILL_BLUR, SubtitleAnimationState.INACTIVE, 2, 6),
            Track(AnimationProperty.STROKE_BLUR, SubtitleAnimationState.INACTIVE, 3, 8));
        document = document with
        {
            FrameRate = new(1, 1),
            Layers = [document.Layers[0] with
            {
                Mask = new VectorClipMask
                {
                    Contours = [new() { Nodes = [new() { Position = new(0, 0) }, new() { Position = new(100, 0) }, new() { Position = new(100, 100) }] }]
                },
                Tracks = document.Layers[0].Tracks.Add(new(AnimationProperty.MASK_POSITION,
                    [new(MediaTime.Zero, new ScenePoint()), new(new(2), new ScenePoint(30, 0))]))
            }]
        };
        ProjectValidator.Validate(document);

        var written = AssSubtitleFormat.Write(document);
        var bodies = written.Text.Split('\n').Where(row => row.StartsWith("Dialogue:", StringComparison.Ordinal))
            .Select(row => row.Split(',', 10)[9]).ToArray();

        Assert.Equal(2, bodies.Length);
        Assert.Contains("\\t(0,1000,1,\\blur6)", bodies[1], StringComparison.Ordinal);
        Assert.DoesNotContain("\\blur8", bodies[1], StringComparison.Ordinal);
        if (!staticOverride)
        {
            Assert.Contains("\\t(0,1000,1,\\bord4)", bodies[1], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PreferredShadowBlurStillAnimatesWhenOnlyTheOtherStateHasAnEdgeBlurTrack()
    {
        var written = AssSubtitleFormat.Write(Document(Line(),
            Track(AnimationProperty.STROKE_BLUR, SubtitleAnimationState.ACTIVE, 12, 16),
            Track(AnimationProperty.SHADOW_BLUR, SubtitleAnimationState.INACTIVE, 2, 6)));

        Assert.Contains(Transform(AnimationProperty.SHADOW_BLUR, 6), Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain(Transform(AnimationProperty.STROKE_BLUR, 16), Body(written.Text), StringComparison.Ordinal);
        Assert.Single(Regex.Matches(Body(written.Text), @"\\t\("));
        Assert.Contains(written.Diagnostics, item => item.Code == "Ass.BlurAnimation");
    }

    [Theory]
    [InlineData(KaraokeHighlightKind.STEP)]
    [InlineData(KaraokeHighlightKind.OUTLINE_STEP)]
    public void ActivationInstantKeepsUnmanagedPropertiesButDoesNotOverwriteASharedAnimatedProperty(KaraokeHighlightKind kind)
    {
        var line = Line() with
        {
            Karaoke = [new(0, 2, new(1, 4), new(3, 4), SceneColor.White) { HighlightKind = kind }],
            KaraokeStyleSpans = [new(0, 2, new() { StrokeWidth = 40, ShadowOffset = new(90, 91) })]
        };
        var written = AssSubtitleFormat.Write(Document(line, Track(AnimationProperty.SHADOW_OFFSET, SubtitleAnimationState.INACTIVE, 2, 6)));

        Assert.Contains("\\t(250,250,\\bord40)", Body(written.Text), StringComparison.Ordinal);
        Assert.Contains(Transform(AnimationProperty.SHADOW_OFFSET, 6), Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain("\\xshad90", Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain("\\yshad91", Body(written.Text), StringComparison.Ordinal);
        Assert.Contains(kind == KaraokeHighlightKind.STEP ? "\\k50" : "\\ko50", Body(written.Text), StringComparison.Ordinal);
    }

    [Fact]
    public void NormalNumericAndUniformScaleCompensationCannotDuplicateTheSharedBorderAnimation()
    {
        var scale = new AnimationTrack(AnimationProperty.SCALE,
            [new(MediaTime.Zero, new ScenePoint(1, 1)), new(new(2), new ScenePoint(2, 2))]);
        var written = AssSubtitleFormat.Write(Document(Line(), scale,
            Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.NORMAL, 22, 26),
            Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.INACTIVE, 2, 6)));

        Assert.Contains(Transform(AnimationProperty.STROKE_WIDTH, 6), Body(written.Text), StringComparison.Ordinal);
        Assert.Single(Regex.Matches(Body(written.Text), @"\\t\([^)}]*?\\bord[-+\d.]+[^)}]*\)"));
        Assert.Contains(written.Diagnostics, item => item.Code == "Ass.TransformAppearanceAnimation");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedWidthRetainsItsCurveAndContentClockAcrossOffsetsAndMaskEvents(bool animatedMask)
    {
        var origin = new MediaTime(5, 4);
        var line = Line() with
        {
            Start = new(203, 100), End = new(403, 100),
            Karaoke = [new(0, 2, origin, origin + new MediaTime(2), SceneColor.White) { HighlightKind = KaraokeHighlightKind.SWEEP }]
        };
        var width = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.STROKE_WIDTH, State: SubtitleAnimationState.INACTIVE),
            [new(origin, 2, KeyframeInterpolation.POWER) { Exponent = 2 }, new(origin + new MediaTime(2), 8)]);
        var document = Document(line);
        var layer = document.Layers[0] with
        {
            AnimationOffset = origin,
            Mask = animatedMask ? new VectorClipMask
            {
                Contours = [new() { Nodes = [new() { Position = new(0, 0) }, new() { Position = new(100, 0) }, new() { Position = new(100, 100) }] }]
            } : null,
            Tracks = animatedMask ? [width, new(AnimationProperty.MASK_POSITION,
                [new(origin, new ScenePoint()), new(origin + new MediaTime(2), new ScenePoint(30, 0))])] : [width]
        };
        document = document with { Layers = [layer], FrameRate = new(4, 1) };
        ProjectValidator.Validate(document);
        var offset = new MediaTime(17, 1000);

        var written = AssSubtitleFormat.Write(document, offset);
        var imported = AssSubtitleFormat.Parse(written.Text, 640, 360).Clips;

        Assert.Equal(animatedMask ? 9 : 1, imported.Length);
        foreach (var clip in imported)
        {
            var copied = Assert.Single(clip.Tracks, track => track.Property == AnimationProperty.STROKE_WIDTH);
            Assert.Equal(SubtitleAnimationState.NORMAL, copied.Target.State);
            var duration = clip.Line.End - clip.Line.Start;
            var elapsed = new MediaTime(duration.Numerator, duration.Denominator * 2);
            var clock = clip.Line.Start - offset - line.Start + origin + elapsed;
            Assert.InRange(Math.Abs(SceneEvaluator.EvaluateScalarTrack(width, clock) -
                SceneEvaluator.EvaluateScalarTrack(copied, clip.ContentOffset + elapsed)), 0, 1d / 255 + 1e-6);
        }
        Assert.Contains(written.Diagnostics, item => item.Code == "Ass.KaraokeAnimation");
        Assert.Same(width, document.Layers[0].Tracks[0]);
    }

    [Fact]
    public void HoldTrackRemainsAHoldAndTheActivationInstantCannotReplaceItsLaterChange()
    {
        var line = Line() with
        {
            Karaoke = [new(0, 2, new(1, 4), new(3, 4), SceneColor.White)],
            KaraokeStyleSpans = [new(0, 2, new() { StrokeWidth = 40 })]
        };
        var track = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.STROKE_WIDTH, State: SubtitleAnimationState.INACTIVE),
            [new(MediaTime.Zero, 2, KeyframeInterpolation.HOLD), new(new(1), 6)]);
        var written = AssSubtitleFormat.Write(Document(line, track));

        Assert.Contains("\\t(1000,1000,1,\\bord6)", Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain("\\t(250,250,\\bord40)", Body(written.Text), StringComparison.Ordinal);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips);
        var copied = Assert.Single(imported.Tracks, item => item.Property == AnimationProperty.STROKE_WIDTH);
        Assert.Equal(2, SceneEvaluator.EvaluateScalarTrack(copied, imported.ContentOffset + new MediaTime(1, 2)));
        Assert.Equal(6, SceneEvaluator.EvaluateScalarTrack(copied, imported.ContentOffset + new MediaTime(3, 2)));
    }

    [Fact]
    public void ACompletedNegativeTimeStateTransformRetainsItsVisibleValueWithoutAZeroZeroTransform()
    {
        var track = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.STROKE_WIDTH, State: SubtitleAnimationState.INACTIVE), [])
        {
            InitialValue = 2,
            Transforms = [new(Guid.NewGuid(), new(-1), new(-1, 2), 6)]
        };
        var written = AssSubtitleFormat.Write(Document(Line(), track));

        Assert.DoesNotContain("\\t(0,0,", Body(written.Text), StringComparison.Ordinal);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips);
        var copied = Assert.Single(imported.Tracks, item => item.Property == AnimationProperty.STROKE_WIDTH);
        Assert.Equal(6, SceneEvaluator.EvaluateScalarTrack(copied, imported.ContentOffset));
        Assert.Equal(6, SceneEvaluator.EvaluateScalarTrack(copied, imported.ContentOffset + new MediaTime(1)));
        Assert.Contains(written.Diagnostics, item => item.Code == "Ass.KaraokeAnimation");
    }

    [Fact]
    public void OverlappingKaraokeGroupsKeepTheirIndependentKtClockAndTextWhileSharingTheAppearanceTrack()
    {
        var line = Line() with
        {
            Karaoke =
            [
                new(0, 1, MediaTime.Zero, new(1), SceneColor.White) { HighlightKind = KaraokeHighlightKind.SWEEP },
                new(1, 1, new(1, 2), new(3, 2), SceneColor.White) { HighlightKind = KaraokeHighlightKind.STEP }
            ]
        };
        var written = AssSubtitleFormat.Write(Document(line,
            Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.INACTIVE, 2, 6)));
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);

        Assert.Contains("\\kf100", Body(written.Text), StringComparison.Ordinal);
        Assert.Contains("\\kt50", Body(written.Text), StringComparison.Ordinal);
        Assert.Contains("\\k100", Body(written.Text), StringComparison.Ordinal);
        Assert.Equal(line.Text, imported.Text);
        Assert.Equal(line.Karaoke[0].Start, imported.Karaoke[0].Start);
        Assert.Equal(line.Karaoke[1].Start, imported.Karaoke[1].Start);
    }

    [Fact]
    public void UntimedTextKeepsItsNormalAnimationAndReportsDormantStateWithoutCreatingKaraoke()
    {
        var line = Line() with { Karaoke = [] };
        var written = AssSubtitleFormat.Write(Document(line,
            Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.NORMAL, 2, 6),
            Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.INACTIVE, 12, 16)));

        Assert.Contains(Transform(AnimationProperty.STROKE_WIDTH, 6), Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain(Transform(AnimationProperty.STROKE_WIDTH, 16), Body(written.Text), StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, item => item.Code == "Ass.DormantKaraokeAnimation");
        Assert.DoesNotContain(written.Diagnostics, item => item.Code == "Ass.KaraokeAnimation");
        Assert.Empty(Assert.Single(AssSubtitleFormat.Parse(written.Text).Lines).Karaoke);
    }

    [Fact]
    public void AdvancedAssProjectionDoesNotApplyTheLossySharedStateSelection()
    {
        var line = Line();
        var inactive = Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.INACTIVE, 12, 16);
        var document = Document(line, Track(AnimationProperty.STROKE_WIDTH, SubtitleAnimationState.NORMAL, 2, 6), inactive);
        var layer = document.Layers[0];

        var projection = AssTextProjection.Create(line, layer: layer);
        var applied = AssTextProjection.Apply(line, projection.Source, layer: layer);

        Assert.Contains(Transform(AnimationProperty.STROKE_WIDTH, 6), projection.Source, StringComparison.Ordinal);
        Assert.DoesNotContain(Transform(AnimationProperty.STROKE_WIDTH, 16), projection.Source, StringComparison.Ordinal);
        Assert.Same(line, applied.Line);
        Assert.Same(inactive, layer.Tracks[1]);
    }

    private static AnimationTrack Track(AnimationProperty property, SubtitleAnimationState state, double initial, double target,
        Guid? rangeId = null)
    {
        return new(new AnimationTrackTarget(property, TextRangeId: rangeId, State: state), [])
        {
            ColorSpace = property is AnimationProperty.STROKE or AnimationProperty.SHADOW_COLOR
                ? AnimationColorSpace.SRGB : AnimationColorSpace.LINEAR_RGB,
            InitialValue = Value(property, initial),
            Transforms = [new(Guid.NewGuid(), MediaTime.Zero, new(2), Value(property, target))]
        };
    }

    private static AnimationValue Value(AnimationProperty property, double value)
    {
        return property switch
        {
            AnimationProperty.STROKE or AnimationProperty.SHADOW_COLOR => new SceneColor(value / 32, 0, 0),
            AnimationProperty.SHADOW_OFFSET => new ScenePoint(value, value + 1),
            AnimationProperty.FILL_BLUR or AnimationProperty.STROKE_BLUR or AnimationProperty.SHADOW_BLUR => value * AssBlurConversion.SigmaPerUnit,
            _ => value
        };
    }

    private static string Transform(AnimationProperty property, double target)
    {
        var value = AssFormatValues.Number(target);
        var tags = property switch
        {
            AnimationProperty.STROKE => "\\3c" + AssFormatValues.Color(new(target / 32, 0, 0), false) + "\\3a&H00&",
            AnimationProperty.SHADOW_COLOR => "\\4c" + AssFormatValues.Color(new(target / 32, 0, 0), false) + "\\4a&H00&",
            AnimationProperty.STROKE_WIDTH => "\\bord" + value,
            AnimationProperty.SHADOW_OFFSET => "\\xshad" + value + "\\yshad" + AssFormatValues.Number(target + 1),
            _ => "\\blur" + value
        };
        return "\\t(0,2000,1," + tags + ")";
    }

    private static SubtitleLine Line(double strokeWidth = 1)
    {
        return new()
        {
            Text = "ab", End = new(2), Karaoke = [Segment(2)],
            Style = new()
            {
                Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 },
                FontFamily = "Noto Sans", FontSize = 20, StrokeWidth = strokeWidth,
                ShadowOffset = new(), ShadowColor = SceneColor.Transparent, ShadowBlur = 0,
                WrapMode = SubtitleWrapMode.NO_WRAP, Alignment = TextAlignment.TOP_LEFT,
                Position = new() { Anchor = new(), Pivot = new(), Offset = new(30, 30) }
            }
        };
    }

    private static KaraokeSegment Segment(int length)
    {
        return new(0, length, MediaTime.Zero, new(2), SceneColor.White)
        {
            HighlightKind = KaraokeHighlightKind.SWEEP
        };
    }

    private static ProjectDocument Document(SubtitleLine line, params AnimationTrack[] tracks)
    {
        var document = new ProjectDocument
        {
            Width = 640, Height = 360, Subtitles = [line],
            Layers = [new() { SubtitleId = line.Id, Start = line.Start, End = line.End, Tracks = tracks.ToImmutableArray() }]
        };
        ProjectValidator.Validate(document);
        return document;
    }

    private static string Body(string source)
    {
        return source.Split('\n').Single(row => row.StartsWith("Dialogue:", StringComparison.Ordinal)).Split(',', 10)[9];
    }
}
