using System.Collections.Immutable;
using System.Text.RegularExpressions;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssNumericTransformExportTests
{
    [Fact]
    public void InlineStyleChangesKeepTheWholeLineAnimationAndItsPhase()
    {
        var line = Line() with { InlineSpans = [new(1, 1, new() { LetterSpacing = 88, Bold = true })] };
        var track = new AnimationTrack(AnimationProperty.LETTER_SPACING, [new(new(0), 0), new(new(1), 10)]);
        var written = AssSubtitleFormat.Write(Document(line, track));
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips);
        var importedLayer = Layer(imported.Line) with
        {
            Transform = imported.Transform, Tracks = imported.Tracks, AnimationOffset = imported.ContentOffset
        };
        foreach (var time in new[] { MediaTime.Zero, new(1, 4), new(1, 2), new(999, 1000), new(1), new(1999, 1000) })
        {
            var evaluated = SceneEvaluator.EvaluateLayer(importedLayer, imported.Line, imported.ContentOffset + time);
            foreach (var offset in Enumerable.Range(0, imported.Line.Text.Length))
            {
                var style = imported.Line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= offset &&
                    offset < span.Utf16Start + span.Utf16Length)?.Style.ApplyTo(imported.Line.Style) ?? imported.Line.Style;
                var animated = SubtitleAnimationEvaluation.ApplyTypographyAnimations(evaluated, style, offset);
                Assert.Equal(SceneEvaluator.EvaluateScalarTrack(track, time), animated.LetterSpacing, 8);
                Assert.Equal(offset == 1, style.Bold);
            }
        }
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Subtitle.Composition");
        Assert.Equal(88, line.InlineSpans[0].Style.LetterSpacing);
    }

    [Fact]
    public void PowerAnimationRetainsItsNegativeStartPhaseAfterActualCentisecondRebasing()
    {
        var line = Line() with { Start = new(10), End = new(12) };
        var track = Ordered(AnimationProperty.LETTER_SPACING, 0, new AnimationTransformOperation(Guid.NewGuid(), new(0), new(1), 10, 2));
        var layer = Layer(line) with { AnimationOffset = new(1, 2), Tracks = [track] };
        var written = AssSubtitleFormat.Write(Document(line, layer), timeOffset: new(1, 200));
        Assert.Contains("\\t(-495,505,2,\\fsp10)", Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformCurveApproximation");
    }

    [Fact]
    public void CompletedLaterOrderedOperationRemovesEveryEarlierOperationBeforeEmittingRemainingOnes()
    {
        var line = Line();
        var track = Ordered(AnimationProperty.LETTER_SPACING, 0,
            new(Guid.NewGuid(), new(0), new(2), 99), new(Guid.NewGuid(), new(0), new(1, 4), 3),
            new(Guid.NewGuid(), new(0), new(1), 20, 2));
        var layer = Layer(line) with { AnimationOffset = new(1, 2), Tracks = [track] };
        var body = Body(AssSubtitleFormat.Write(Document(line, layer)).Text);
        Assert.Contains("\\fsp3\\t(-500,500,2,\\fsp20)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\\fsp99", body, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(body, @"\\t\("));
    }

    [Fact]
    public void HoldKeepsItsDelayAndChangesInstantlyAtTheNextKey()
    {
        var track = new AnimationTrack(AnimationProperty.LETTER_SPACING,
            [new(new(0), 2, KeyframeInterpolation.HOLD), new(new(1), 9)]);
        var written = AssSubtitleFormat.Write(Document(Line(), track));
        Assert.Contains("\\fsp2\\t(1000,1000,1,\\fsp9)", Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformCurveApproximation");
    }

    [Theory]
    [InlineData(KeyframeInterpolation.POWER, 0, 3, false)]
    [InlineData(KeyframeInterpolation.POWER, 0.25, 1, true)]
    [InlineData(KeyframeInterpolation.EASE_IN, 0, 2, false)]
    [InlineData(KeyframeInterpolation.EASE_OUT, 0, 1, true)]
    public void OnlyDirectlyRepresentableCurvePhasesReuseTheirExponent(KeyframeInterpolation curve, double start, int exponent, bool approximate)
    {
        var track = new AnimationTrack(AnimationProperty.LETTER_SPACING,
            [new(new(0), 0, curve) { CurveStart = start, CurveEnd = 0.75, Exponent = 3 }, new(new(1), 10)]);
        var written = AssSubtitleFormat.Write(Document(Line(), track));
        if (approximate)
        {
            Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.AnimationSampling");
            Assert.Contains("\\fsp10", Body(written.Text), StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("\\t(0,1000," + exponent + ",\\fsp10)", Body(written.Text), StringComparison.Ordinal);
            Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformCurveApproximation");
        }
    }

    [Fact]
    public void IndependentScaleComponentCurvesAndZeroEntranceAreRetained()
    {
        var track = new AnimationTrack(AnimationProperty.SCALE,
        [
            new(new(0), new ScenePoint(0, 0), KeyframeInterpolation.POWER)
            {
                Exponent = 2, ComponentCurves = [new(KeyframeInterpolation.POWER) { Exponent = 3 }]
            },
            new(new(1), new ScenePoint(1, 2))
        ]);
        var written = AssSubtitleFormat.Write(Document(Line(), track));
        var body = Body(written.Text);
        Assert.Contains("\\fscx0\\t(0,1000,2,\\fscx100)", body, StringComparison.Ordinal);
        Assert.Contains("\\fscy0\\t(0,1000,3,\\fscy200)", body, StringComparison.Ordinal);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code is "Ass.TransformScale" or "Subtitle.Composition");
    }

    [Fact]
    public void CustomNativePivotRejectsOnlyTheScaleAxisThatNeedsAnimatedTranslation()
    {
        var line = Line();
        var scale = new AnimationTrack(AnimationProperty.SCALE, [new(new(0), new ScenePoint(1, 1)), new(new(1), new ScenePoint(2, 3))]);
        var spacing = new AnimationTrack(AnimationProperty.LETTER_SPACING, [new(new(0), 0), new(new(1), 5)]);
        var layer = Layer(line) with { Transform = new() { Pivot = new(10, 0) }, Tracks = [scale, spacing] };
        var written = AssSubtitleFormat.Write(Document(line, layer));
        var body = Body(written.Text);
        Assert.DoesNotContain("\\t(0,1000,1,\\fscx200)", body, StringComparison.Ordinal);
        Assert.Contains("\\t(0,1000,1,\\fscy300)", body, StringComparison.Ordinal);
        Assert.Contains("\\t(0,1000,1,\\fsp5)", body, StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformPivotAnimation");
    }

    [Fact]
    public void ADefaultAutomaticallyPositionedLineStillExportsItsSpacingAnimation()
    {
        var line = Line() with { Style = Line().Style with { Position = null, WrapMode = SubtitleWrapMode.GRAPHEME } };
        var track = new AnimationTrack(AnimationProperty.LETTER_SPACING, [new(new(0), 0), new(new(1), 10)]);
        var written = AssSubtitleFormat.Write(Document(line, track));
        Assert.Contains("\\t(0,1000,1,\\fsp10)", Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformPivotAnimation");
    }

    [Fact]
    public void NegativeScaleAnimationDropsOnlyItsOwnAxis()
    {
        var track = new AnimationTrack(AnimationProperty.SCALE,
            [new(new(0), new ScenePoint(-1, 1)), new(new(1), new ScenePoint(1, 2))]);
        var written = AssSubtitleFormat.Write(Document(Line(), track));
        Assert.Contains("\\t(0,1000,1,\\fscy200)", Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain("\\fscx-", Body(written.Text), StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformScale");
    }

    [Fact]
    public void SharedClockOrderedScaleAxesRoundTripOverlappingOperationsInTheirSourceOrder()
    {
        var track = new AnimationTrack(AnimationProperty.SCALE, [])
        {
            InitialValue = new ScenePoint(1, 2),
            Transforms =
            [
                new(Guid.NewGuid(), new(0), new(1), new ScenePoint(2, 3), 2),
                new(Guid.NewGuid(), new(1, 2), new(3, 2), new ScenePoint(4, 5))
            ]
        };
        var written = AssSubtitleFormat.Write(Document(Line(), track));
        var clip = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips);
        var imported = Assert.Single(clip.Tracks, candidate => candidate.Property == AnimationProperty.SCALE);
        foreach (var time in new[] { new MediaTime(0), new(1, 4), new(3, 4), new(5, 4), new(7, 4) })
        {
            var expected = SceneEvaluator.EvaluateVectorTrack(track, time);
            var actual = SceneEvaluator.EvaluateVectorTrack(imported, clip.ContentOffset + time);
            Assert.Equal(expected.X, actual.X, 8);
            Assert.Equal(expected.Y, actual.Y, 8);
        }
        Assert.DoesNotContain(clip.Tracks, candidate => candidate.Property is AnimationProperty.SCALE_X or AnimationProperty.SCALE_Y);
    }

    [Fact]
    public void UniformScaleDerivesStaticBorderAndBlurCompensationWithoutDroppingGeometry()
    {
        var line = Line() with { Style = Line().Style with { StrokeWidth = 2, StrokeBlur = AssBlurConversion.SigmaPerUnit } };
        var scale = new AnimationTrack(AnimationProperty.SCALE, [new(new(0), new ScenePoint(0, 0)), new(new(1), new ScenePoint(2, 2))]);
        var written = AssSubtitleFormat.Write(Document(line, scale));
        var body = Body(written.Text);
        Assert.Contains("\\bord0\\t(0,1000,1,\\bord4)", body, StringComparison.Ordinal);
        Assert.Contains("\\blur0\\t(0,1000,1,\\blur2)", body, StringComparison.Ordinal);
        Assert.Contains("\\t(0,1000,1,\\fscx200)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void RotationRetainsGeometryAndReportsItsNonlinearShadowCompensation()
    {
        var line = Line() with { Style = Line().Style with { ShadowColor = SceneColor.Black, ShadowOffset = new(4, 2) } };
        var track = new AnimationTrack(AnimationProperty.ROTATION, [new(new(0), 0), new(new(1), 90)]);
        var written = AssSubtitleFormat.Write(Document(line, track));
        Assert.Contains("\\frz0\\t(0,1000,1,\\frz-90)", Body(written.Text), StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformAppearanceAnimation");
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Subtitle.Composition");
    }

    [Fact]
    public void StaticScaleCompensatesBorderAndSelectedBlurAnimationTargets()
    {
        var line = Line();
        var layer = Layer(line) with
        {
            Transform = new() { Scale = new(2, 2) },
            Tracks =
            [
                new(AnimationProperty.STROKE_WIDTH, [new(new(0), 1), new(new(1), 4)]),
                new(AnimationProperty.STROKE_BLUR, [new(new(0), AssBlurConversion.SigmaPerUnit), new(new(1), 3 * AssBlurConversion.SigmaPerUnit)])
            ]
        };
        var body = Body(AssSubtitleFormat.Write(Document(line, layer)).Text);
        Assert.Contains("\\bord2\\t(0,1000,1,\\bord8)", body, StringComparison.Ordinal);
        Assert.Contains("\\blur2\\t(0,1000,1,\\blur6)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BlurAnimationReportsItsCoupledShadowEvenWhenInitialBlurValuesMatch()
    {
        var line = Line() with { Style = Line().Style with { ShadowColor = SceneColor.Black, FillBlur = 0, ShadowBlur = 0 } };
        var track = new AnimationTrack(AnimationProperty.FILL_BLUR, [new(new(0), 0), new(new(1), AssBlurConversion.SigmaPerUnit)]);
        var written = AssSubtitleFormat.Write(Document(line, track));
        Assert.Contains("\\blur0\\t(0,1000,1,\\blur1)", Body(written.Text), StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShadowBlur");
    }

    [Fact]
    public void ASubmillisecondFutureEndNeverEmitsTheSpecialZeroDurationEndpoint()
    {
        var track = new AnimationTrack(AnimationProperty.LETTER_SPACING, [new(new(0), 0), new(new(1, 10000), 10)]);
        var written = AssSubtitleFormat.Write(Document(Line(), track));
        Assert.Contains("\\t(0,1,1,\\fsp10)", Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain("\\t(0,0,", Body(written.Text), StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformTimeQuantization");
    }

    [Fact]
    public void ExplicitKaraokeVisualsSkipOnlyTheConflictingWholeLineAnimation()
    {
        var line = Line() with
        {
            Karaoke = [new(0, 1, new(1, 4), new(3, 4), SceneColor.White)
            {
                HighlightKind = KaraokeHighlightKind.STEP
            }],
            KaraokeStyleSpans = [new(0, 1, new() { StrokeWidth = 6 })]
        };
        var layer = Layer(line) with
        {
            Tracks = [new(AnimationProperty.STROKE_WIDTH, [new(new(0), 1), new(new(1), 4)]),
                new(AnimationProperty.LETTER_SPACING, [new(new(0), 0), new(new(1), 10)])]
        };
        var written = AssSubtitleFormat.Write(Document(line, layer));
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.KaraokeAnimation");
        Assert.Contains("\\t(0,1000,1,\\fsp10)", Body(written.Text), StringComparison.Ordinal);
        Assert.DoesNotContain("\\t(0,1000,1,\\bord4)", Body(written.Text), StringComparison.Ordinal);
    }

    private static AnimationTrack Ordered(AnimationProperty property, double initial, params AnimationTransformOperation[] operations)
    {
        return new(property, [])
        {
            InitialValue = initial,
            Transforms = operations.ToImmutableArray()
        };
    }

    private static SubtitleLine Line()
    {
        return new()
        {
            Text = "ab", End = new(2), Style = new()
            {
                Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 },
                Alignment = TextAlignment.TOP_LEFT,
                Position = new() { Anchor = new(0, 0), Pivot = new(0, 0), Offset = new(20, 20) },
                WrapMode = SubtitleWrapMode.NO_WRAP, StrokeWidth = 0, ShadowBlur = 0, ShadowColor = SceneColor.Transparent,
                ShadowOffset = default
            }
        };
    }

    private static ProjectLayer Layer(SubtitleLine line)
    {
        return new() { SubtitleId = line.Id, Start = line.Start, End = line.End };
    }

    private static ProjectDocument Document(SubtitleLine line, AnimationTrack track)
    {
        return Document(line, Layer(line) with { Tracks = [track] });
    }

    private static ProjectDocument Document(SubtitleLine line, ProjectLayer layer)
    {
        return new() { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] };
    }

    private static string Body(string text)
    {
        return Assert.Single(text.Split('\n'), row => row.StartsWith("Dialogue:", StringComparison.Ordinal)).Split(',', 10)[9];
    }
}
