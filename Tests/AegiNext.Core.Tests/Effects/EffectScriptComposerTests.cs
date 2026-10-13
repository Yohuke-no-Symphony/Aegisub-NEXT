using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptComposerTests
{
    [Theory]
    [InlineData(0.125)]
    [InlineData(1.75)]
    [InlineData(12.5)]
    public void OverlayInsidePowerCurvePreservesBothUncoveredCurveSubranges(double exponent)
    {
        var original = new AnimationTrack(AnimationProperty.ROTATION,
        [
            new(new(0), 12, KeyframeInterpolation.POWER) { Exponent = exponent, CurveStart = 0.2, CurveEnd = 0.9 },
            new(new(4), 212)
        ]);
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(4), Tracks = [original] };
        var script = MiddleScript(original, EffectScriptProperty.ROTATION, 500);

        var composed = Assert.Single(EffectScriptComposer.Compose(script, layer));

        AssertUncoveredSamplesEqual(original, composed, new(3, 2), new(5, 2), new(4));
        Assert.Equal(500, SceneEvaluator.EvaluateVector3Track(composed, new(2)).Z);
        Assert.Equal(exponent, composed.Keyframes[0].GetCurve(2).Exponent);
        Assert.Equal(exponent, composed.Keyframes.Single(frame => frame.Time == new MediaTime(5, 2)).GetCurve(2).Exponent);
        Validate(layer, [composed]);
    }

    [Fact]
    public void OverlayInsideVectorCurvePreservesIndependentComponentCurvePhases()
    {
        var original = new AnimationTrack(AnimationProperty.POSITION,
        [
            new(new(0), new ScenePoint(0, 80), KeyframeInterpolation.POWER)
            {
                Exponent = 2.5,
                CurveStart = 0.1,
                CurveEnd = 0.9,
                ComponentCurves = [new(KeyframeInterpolation.EASE_IN_OUT, 0.2, 0.8)]
            },
            new(new(4), new ScenePoint(40, -40))
        ]);
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(4), Tracks = [original] };
        var changed = new ScenePoint(100, -300);
        var script = MiddleScript(original, EffectScriptProperty.POSITION, changed);

        var composed = Assert.Single(EffectScriptComposer.Compose(script, layer));

        AssertUncoveredSamplesEqual(original, composed, new(3, 2), new(5, 2), new(4));
        Assert.Equal(changed, SceneEvaluator.EvaluateVectorTrack(composed, new(2)));
        Assert.Equal(KeyframeInterpolation.EASE_IN_OUT, composed.Keyframes[0].GetCurve(1).Interpolation);
        Assert.Equal(KeyframeInterpolation.EASE_IN_OUT,
            composed.Keyframes.Single(frame => frame.Time == new MediaTime(5, 2)).GetCurve(1).Interpolation);
        Validate(layer, [composed]);
    }

    [Fact]
    public void OverlayInsideHdrColorCurvePreservesAllIndependentComponentCurves()
    {
        var original = new AnimationTrack(AnimationProperty.FILL,
        [
            new(new(0), new SceneColor(-0.5, 4, 8, 0.2), KeyframeInterpolation.POWER)
            {
                Exponent = 0.7,
                CurveStart = 0.1,
                CurveEnd = 0.9,
                ComponentCurves =
                [
                    new(KeyframeInterpolation.EASE_OUT, 0.15, 0.7),
                    null,
                    new(KeyframeInterpolation.POWER, 0.2, 0.9) { Exponent = 3.5 }
                ]
            },
            new(new(4), new SceneColor(3, 1, -2, 0.8))
        ]);
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(4), Tracks = [original] };
        var changed = new SceneColor(12, -3, 5, 0.4);
        var script = MiddleScript(original, EffectScriptProperty.FILL, changed);

        var composed = Assert.Single(EffectScriptComposer.Compose(script, layer));

        AssertUncoveredSamplesEqual(original, composed, new(3, 2), new(5, 2), new(4));
        Assert.Equal(changed, SceneEvaluator.EvaluateColorTrack(composed, new(2)));
        Assert.Equal(3.5, composed.Keyframes[0].GetCurve(3).Exponent);
        Assert.Equal(3.5, composed.Keyframes.Single(frame => frame.Time == new MediaTime(5, 2)).GetCurve(3).Exponent);
        Validate(layer, [composed]);
    }

    [Fact]
    public void MultipleDeclaredIntervalsPreserveLeadingIntermediateAndTrailingEmptySegments()
    {
        var script = EffectScriptParser.Parse("""
            effect "separate-overlays" version 1
            short-clip compress
            segment before flex 1
            end
            segment first fixed 1s
                at 0 rotation 2
                at 0.5 rotation 30
                at 1 rotation 4
            end
            segment middle flex 1
            end
            segment second fixed 1s
                at 0 rotation 6
                at 0.5 rotation -30
                at 1 rotation 8
            end
            segment after flex 1
            end
            """);
        var original = new AnimationTrack(AnimationProperty.ROTATION,
        [
            new(new(0), 0), new(new(1), 2), new(new(2), 4),
            new(new(5, 2), 14), new(new(3), 6), new(new(4), 8), new(new(5), 10)
        ]);
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(5), Tracks = [original] };

        var composed = Assert.Single(EffectScriptComposer.Compose(script, layer));

        for (var sample = 0; sample <= 1000; sample++)
        {
            var time = new MediaTime(sample, 200);
            if (time <= new MediaTime(1) || time >= new MediaTime(2) && time <= new MediaTime(3) || time >= new MediaTime(4))
            {
                AssertValueEqual(SceneEvaluator.EvaluateTrack(original, time), SceneEvaluator.EvaluateTrack(composed, time));
            }
        }

        Assert.Equal(30, SceneEvaluator.EvaluateVector3Track(composed, new(3, 2)).Z);
        Assert.Equal(14, SceneEvaluator.EvaluateVector3Track(composed, new(5, 2)).Z);
        Assert.Equal(-30, SceneEvaluator.EvaluateVector3Track(composed, new(7, 2)).Z);
        Validate(layer, [composed]);
    }

    [Fact]
    public void OverlayPreservesExistingHoldJumpsAndOutgoingSuffixCurve()
    {
        var original = new AnimationTrack(AnimationProperty.ROTATION,
        [
            new(new(0), 10, KeyframeInterpolation.HOLD),
            new(new(1), 20, KeyframeInterpolation.EASE_OUT),
            new(new(2), 30, KeyframeInterpolation.HOLD),
            new(new(3), 40, KeyframeInterpolation.EASE_IN),
            new(new(4), 50)
        ]);
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(4), Tracks = [original] };

        var composed = Assert.Single(EffectScriptComposer.Compose(MiddleScript(original, EffectScriptProperty.ROTATION, 100), layer));

        AssertUncoveredSamplesEqual(original, composed, new(3, 2), new(5, 2), new(4));
        Assert.Equal(10, SceneEvaluator.EvaluateVector3Track(composed, new(999, 1000)).Z);
        Assert.Equal(20, SceneEvaluator.EvaluateVector3Track(composed, new(1)).Z);
        Assert.Equal(30, SceneEvaluator.EvaluateVector3Track(composed, new(2999, 1000)).Z);
        Assert.Equal(40, SceneEvaluator.EvaluateVector3Track(composed, new(3)).Z);
        Validate(layer, [composed]);
    }

    [Fact]
    public void NodeOverlayMatchesStableTargetAndPreservesAnotherNodesSamePropertyTrack()
    {
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(100, 30) };
        var mask = new VectorClipMask { Contours = [new() { Nodes = [first, second] }] };
        var firstTrack = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id),
            [new(new(0), first.Position), new(new(4), first.Position)]);
        var secondTrack = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id),
            [new(new(0), second.Position), new(new(4), new ScenePoint(200, 60))]);
        var handleTrack = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.MASK_NODE_IN_HANDLE, first.Id),
            [new(new(0), new ScenePoint(-2, 4))]);
        var line = new SubtitleLine { End = new(4) };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE,
            SubtitleId = line.Id,
            End = line.End,
            Mask = mask,
            Tracks = [firstTrack, secondTrack, handleTrack]
        };
        var script = EffectScriptParser.Parse("""
            effect "one-node-overlay" version 1
            short-clip compress
            segment before flex 1
            end
            segment middle fixed 1s
                at 0 mask-node(1,1).position base
                at 0.5 mask-node(1,1).position offset(5, 10)
                at 1 mask-node(1,1).position base
            end
            segment after flex 1
            end
            """);

        var composed = EffectScriptComposer.Compose(script, layer);

        Assert.Equal(3, composed.Length);
        Assert.Same(secondTrack, composed.Single(track => track.Target == secondTrack.Target));
        Assert.Same(handleTrack, composed.Single(track => track.Target == handleTrack.Target));
        var changed = composed.Single(track => track.Target == firstTrack.Target);
        Assert.Equal(first.Id, changed.Target.NodeId);
        AssertUncoveredSamplesEqual(firstTrack, changed, new(3, 2), new(5, 2), new(4));
        var evaluated = Assert.IsType<VectorClipMask>(SceneEvaluator.EvaluateMask(layer with { Tracks = composed }, new(2)));
        Assert.Equal(new ScenePoint(15, 30), evaluated.Contours[0].Nodes[0].Position);
        Assert.Equal(new ScenePoint(150, 45), evaluated.Contours[0].Nodes[1].Position);
        Assert.Equal(mask.Contours[0].Id, evaluated.Contours[0].Id);
        ProjectValidator.Validate(new() { Subtitles = [line], Layers = [layer with { Tracks = composed }] });
    }

    [Theory]
    [InlineData("fade-in")]
    [InlineData("fade-out")]
    [InlineData("fade-in-out")]
    [InlineData("pop-in")]
    [InlineData("pop-out")]
    [InlineData("slide-in")]
    [InlineData("slide-out")]
    public void ComposingWithoutExistingTargetTrackMatchesStandaloneCompilation(string id)
    {
        var script = BuiltinEffectScripts.Get(id).Script;
        foreach (var duration in new MediaTime[] { new(1, 5), new(4) })
        {
            var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = duration, AnimationOffset = new(-3), Opacity = 0.8, Transform = new(100, 200, 2, 3) };
            var expected = EffectScriptCompiler.Compile(script, layer);

            var composed = EffectScriptComposer.Compose(script, layer);

            Assert.Equal(expected.Length, composed.Length);
            foreach (var track in expected)
            {
                var actual = composed.Single(candidate => candidate.Target == track.Target);
                Assert.Equal(track.Keyframes.ToArray(), actual.Keyframes.ToArray());
            }

            Validate(layer, composed);
        }
    }

    [Fact]
    public void IsolatedZeroLengthDeclarationRejectsADifferentExistingValue()
    {
        var original = new AnimationTrack(AnimationProperty.OPACITY, [new(new(0), 0.5)]);
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(2), Tracks = [original] };

        Assert.Throws<EffectScriptException>(() => EffectScriptComposer.Compose(ZeroLengthOpacityScript(), layer));

        Assert.Same(original, Assert.Single(layer.Tracks));
    }

    [Fact]
    public void IsolatedZeroLengthDeclarationWithTheSameValueKeepsTheOldOutgoingCurve()
    {
        var original = new AnimationTrack(AnimationProperty.OPACITY,
        [
            new(new(0), 0.2, KeyframeInterpolation.EASE_OUT),
            new(new(1), 1, KeyframeInterpolation.POWER) { Exponent = 2.5 },
            new(new(2), 0.5)
        ]);
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(2), Tracks = [original] };

        var composed = EffectScriptComposer.Compose(ZeroLengthOpacityScript(), layer);

        var actual = composed.Single(track => track.Target == original.Target);
        Assert.Same(original, actual);
        for (var sample = 0; sample <= 400; sample++)
        {
            var time = new MediaTime(sample, 200);
            AssertValueEqual(SceneEvaluator.EvaluateTrack(original, time), SceneEvaluator.EvaluateTrack(actual, time));
        }

        Validate(layer, composed);
    }

    [Fact]
    public void MatchingZeroLengthDeclarationDoesNotPadASingleKeyExistingTrack()
    {
        var original = new AnimationTrack(AnimationProperty.OPACITY, [new(new(1, 2), 1)]);
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(2), Tracks = [original] };

        var composed = EffectScriptComposer.Compose(ZeroLengthOpacityScript(), layer);

        var actual = composed.Single(track => track.Target == original.Target);
        Assert.Same(original, actual);
        Assert.Single(actual.Keyframes);
        Assert.Equal(new MediaTime(1, 2), actual.Keyframes[0].Time);
        Validate(layer, composed);
    }

    [Fact]
    public void OverlayThatExceedsAValidExistingTracksEntryBudgetIsRejected()
    {
        var original = new AnimationTrack(AnimationProperty.OPACITY,
            Enumerable.Range(0, AnimationPropertyMetadata.GetMaximumTrackEntries(AnimationProperty.OPACITY))
                .Select(index => new Keyframe(new(index, 2500), 0.8)).ToImmutableArray());
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(4), Opacity = 0.8, Tracks = [original] };
        Validate(layer, layer.Tracks);
        var script = EffectScriptParser.Parse("""
            effect "budget-overlay" version 1
            short-clip compress
            segment before flex 1
            end
            segment middle fixed 0.000001ms
                at 0 opacity base
                at 0.5 opacity 0.2
                at 1 opacity base
            end
            segment after flex 1
            end
            """);

        var error = Assert.Throws<EffectScriptException>(() => EffectScriptComposer.Compose(script, layer));

        Assert.Contains("预算", error.Message, StringComparison.Ordinal);
        Assert.Same(original, Assert.Single(layer.Tracks));
    }

    [Fact]
    public void ZeroLengthDeclarationInsideMergedFullCoverageDoesNotCompareAgainstReplacedOldAnimation()
    {
        var original = new AnimationTrack(AnimationProperty.OPACITY, [new(new(0), 0.5)]);
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(2), Tracks = [original] };
        var script = EffectScriptParser.Parse("""
            effect "collapsed-stay" version 1
            short-clip compress
            segment enter fixed 1s
                at 0 opacity 0
                at 1 opacity base
            end
            segment stay flex 1
                at 0 opacity base hold
                at 1 opacity base
            end
            segment exit fixed 1s
                at 0 opacity base ease-in
                at 1 opacity 0
            end
            """);

        var composed = Assert.Single(EffectScriptComposer.Compose(script, layer));

        Assert.Equal(new MediaTime[] { new(0), new(1), new(2) }, composed.Keyframes.Select(frame => frame.Time));
        Assert.Equal(new[] { 0d, 1, 0 }, composed.Keyframes.Select(frame => frame.Value.Scalar));
        Assert.Equal(KeyframeInterpolation.EASE_IN, composed.Keyframes[1].Interpolation);
        Validate(layer, [composed]);
    }

    [Fact]
    public void SingleKeyTargetExtendsItsConstantValueAndUnrelatedOrderedTrackKeepsItsIdentity()
    {
        var original = new AnimationTrack(AnimationProperty.OPACITY, [new(new(2), 0.8)]);
        var ordered = new AnimationTrack(AnimationProperty.ROTATION_3D, [])
        {
            InitialValue = AnimationValue.FromVector3(new(0, 0, 15)),
            Transforms = [new(Guid.NewGuid(), new(0), new(4), AnimationValue.FromVector3(new(0, 0, 30))) { ComponentMask = 4 }]
        };
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(4), Tracks = [original, ordered] };

        var composed = EffectScriptComposer.Compose(MiddleScript(original, EffectScriptProperty.OPACITY, 0.2), layer);

        var actual = composed.Single(track => track.Target == original.Target);
        AssertUncoveredSamplesEqual(original, actual, new(3, 2), new(5, 2), new(4));
        Assert.Equal(0.2, SceneEvaluator.EvaluateScalarTrack(actual, new(2)));
        Assert.Same(ordered, composed.Single(track => track.Target == ordered.Target));
        Validate(layer, composed);
    }

    private static EffectScript MiddleScript(AnimationTrack track, EffectScriptProperty property, AnimationValue middle)
    {
        var first = SceneEvaluator.EvaluateTrack(track, new(3, 2));
        var last = SceneEvaluator.EvaluateTrack(track, new(5, 2));
        return new("middle-overlay", EffectScriptShortClipPolicy.COMPRESS,
        [
            new("before", null, 1, []),
            new("middle", new(1), 0,
            [
                new(0, property, new(EffectScriptValueKind.ABSOLUTE, first)),
                new(0.5m, property, new(EffectScriptValueKind.ABSOLUTE, middle)),
                new(1, property, new(EffectScriptValueKind.ABSOLUTE, last))
            ]),
            new("after", null, 1, [])
        ]);
    }

    private static EffectScript ZeroLengthOpacityScript()
    {
        return EffectScriptParser.Parse("""
            effect "isolated-point" version 1
            short-clip compress
            segment before fixed 1s
                at 0 rotation base
                at 1 rotation base
            end
            segment point flex 1
                at 0 opacity base hold
                at 1 opacity base
            end
            segment after fixed 1s
                at 0 rotation base
                at 1 rotation base
            end
            """);
    }

    private static void AssertUncoveredSamplesEqual(AnimationTrack expected, AnimationTrack actual, MediaTime start, MediaTime end, MediaTime duration)
    {
        for (var sample = 0; new MediaTime(sample, 200) <= duration; sample++)
        {
            var time = new MediaTime(sample, 200);
            if (time <= start || time >= end)
            {
                AssertValueEqual(SceneEvaluator.EvaluateTrack(expected, time), SceneEvaluator.EvaluateTrack(actual, time));
            }
        }

        AssertValueEqual(SceneEvaluator.EvaluateTrack(expected, start), SceneEvaluator.EvaluateTrack(actual, start));
        AssertValueEqual(SceneEvaluator.EvaluateTrack(expected, end), SceneEvaluator.EvaluateTrack(actual, end));
    }

    private static void AssertValueEqual(AnimationValue expected, AnimationValue actual)
    {
        if (expected.Kind == AnimationValueKind.SCALAR && actual.IsVector3)
        {
            Assert.Equal(0, actual.Vector3.X);
            Assert.Equal(0, actual.Vector3.Y);
            actual = AnimationValue.FromScalar(actual.Vector3.Z);
        }
        Assert.Equal(expected.Kind, actual.Kind);
        for (var component = 0; component < expected.ComponentCount; component++)
        {
            Assert.InRange(Math.Abs(expected.GetComponent(component) - actual.GetComponent(component)), 0, 1e-9);
        }
    }

    private static void Validate(ProjectLayer layer, ImmutableArray<AnimationTrack> tracks)
    {
        ProjectValidator.Validate(new() { Layers = [layer with { Tracks = tracks }] });
    }
}
