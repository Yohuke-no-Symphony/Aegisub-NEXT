using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptProjectionTests
{
    [Theory]
    [InlineData(1, 1000)]
    [InlineData(5, 1)]
    public void DocumentedSpatialSampleReturnsToTheNativeBaseAtShortAndLongDurations(long numerator, long denominator)
    {
        var script = EffectScriptRegressionParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "spatial-tilt.aegifx")));
        var duration = new MediaTime(numerator, denominator);
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 40), End = duration,
            Transform = new() { RotationX = 10, RotationY = -20, Rotation = 720, PerspectiveDistance = 500 }
        };
        var result = EffectScriptCompiler.CompileTarget(script, layer);

        Assert.Equal("spatial-tilt", script.Id);
        Assert.Equal(2, result.Tracks.Length);
        var rotation = Assert.Single(result.Tracks, track => track.Property == AnimationProperty.ROTATION_3D);
        Assert.Equal(new SceneVector3(40, -65, 735), SceneEvaluator.EvaluateTrack(rotation, duration / 2).Vector3);
        Assert.Equal(375, SceneEvaluator.EvaluateScalarTrack(result.Tracks.Single(track => track.Property == AnimationProperty.PERSPECTIVE_DISTANCE), duration / 2), 9);
        Assert.All(result.Tracks, track => Assert.Equal(track.Keyframes[0].Value, track.Keyframes[^1].Value));
        Assert.All(result.Tracks, track => Assert.Equal(duration, track.Keyframes[^1].Time));
        ProjectValidator.Validate(new() { Layers = [layer with { Tracks = result.Tracks }] });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ProjectionPropertiesCompileAsOneVector3TrackWithoutWrappingAngles(int version)
    {
        var script = EffectScriptRegressionParser.Parse(Source(version));
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE,
            Shape = new(ShapeKind.RECTANGLE, 100, 40),
            End = new(4)
        };

        var result = EffectScriptCompiler.CompileTarget(script, layer);

        Assert.Equal(2, result.Tracks.Length);
        AssertRotation(result.Tracks.Single(track => track.Property == AnimationProperty.ROTATION_3D),
            default, new(360, -45, 0), new(720, -90, 0));
        AssertScalar(result.Tracks.Single(track => track.Property == AnimationProperty.PERSPECTIVE_DISTANCE), 312.5, 468.75, 625);
        ProjectValidator.Validate(new() { Layers = [layer with { Tracks = result.Tracks }] });
    }

    [Fact]
    public void RelativeProjectionFramesUseOriginalNativeBaseValues()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 40), End = new(4),
            Transform = new() { RotationX = 30, RotationY = -20, PerspectiveDistance = 500 }
        };
        var result = EffectScriptCompiler.CompileTarget(EffectScriptRegressionParser.Parse(Source(2)), layer);

        AssertRotation(result.Tracks.Single(track => track.Property == AnimationProperty.ROTATION_3D),
            new(30, -20, 0), new(390, -65, 0), new(750, -110, 0));
        AssertScalar(result.Tracks.Single(track => track.Property == AnimationProperty.PERSPECTIVE_DISTANCE), 500, 750, 1000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000000001)]
    public void PerspectiveDistanceRejectsInvalidResolvedFrames(double distance)
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 40), End = new(4) };
        var source = Source(2).Replace("perspective-distance factor(2)",
            "perspective-distance " + distance.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

        Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.CompileTarget(EffectScriptRegressionParser.Parse(source), layer));
        Assert.Empty(layer.Tracks);
    }

    [Fact]
    public void GroupedProjectionUsesNormalRangesAndPreservesUnrelatedRotation()
    {
        var line = new SubtitleLine { Text = "AB", End = new(4) };
        var rotation = new AnimationTrack(AnimationProperty.ROTATION, [new(new(0), 25d), new(new(4), 50d)]);
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [rotation] };
        var script = EffectScriptRegressionParser.Parse(Source(2).Replace("scope whole current", "scope whole current\nunit grapheme", StringComparison.Ordinal));

        var result = EffectScriptComposer.ComposeTarget(script, layer, subtitle: line);

        Assert.Equal(2, result.Subtitle!.AnimationRanges.Length);
        AssertRotation(result.Tracks.Single(track => track.Property == AnimationProperty.ROTATION_3D && track.Target.TextRangeId is null),
            new(0, 0, 25), new(0, 0, 37.5), new(0, 0, 50));
        foreach (var range in result.Subtitle.AnimationRanges)
        {
            var tracks = result.Tracks.Where(track => track.Target.TextRangeId == range.Id).ToArray();
            Assert.Equal(2, tracks.Length);
            Assert.All(tracks, track => Assert.Equal(SubtitleAnimationState.NORMAL, track.Target.State));
            AssertRotation(tracks.Single(track => track.Property == AnimationProperty.ROTATION_3D),
                default, new(360, -45, 0), new(720, -90, 0));
        }
        ProjectValidator.Validate(new()
        {
            Subtitles = [result.Subtitle],
            Layers = [layer with { Tracks = result.Tracks }]
        });
    }

    [Theory]
    [InlineData("active")]
    [InlineData("inactive")]
    public void ProjectionPropertiesRejectKaraokeStates(string state)
    {
        var line = new SubtitleLine { Text = "AB", End = new(4) };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End };
        var source = Source(2).Replace("scope whole current", $"scope whole current\nstate {state}", StringComparison.Ordinal);
        var script = EffectScriptRegressionParser.Parse(source);

        Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.CompileTarget(script, layer, subtitle: line));
        Assert.Empty(layer.Tracks);
        Assert.Empty(line.AnimationRanges);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ScalarAuthorSyntaxRetainsIndependentCurvesInTheVector3TimeUnion(int version)
    {
        var scope = version == 2 ? "scope whole current" : string.Empty;
        var scopeEnd = version == 2 ? "end" : string.Empty;
        var script = EffectScriptRegressionParser.Parse($$"""
            effect "axis-curves" version {{version}}
            short-clip compress
            {{scope}}
            segment turn flex 1
                at 0 rotation-x 0 power(2)
                at 1 rotation-x 100
                at 0 rotation-y 20 hold
                at 0.5 rotation-y 20 linear
                at 1 rotation-y 60
            end
            {{scopeEnd}}
            """);
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 40), End = new(4),
            Transform = new() { Rotation = 735 }
        };

        var track = Assert.Single(EffectScriptCompiler.CompileTarget(script, layer).Tracks);

        Assert.Equal(AnimationProperty.ROTATION_3D, track.Property);
        Assert.Equal(new MediaTime[] { new(0), new(2), new(4) }, track.Keyframes.Select(frame => frame.Time));
        var first = SceneEvaluator.EvaluateTrack(track, new(1)).Vector3;
        var next = SceneEvaluator.EvaluateTrack(track, new(3)).Vector3;
        Assert.Equal(6.25, first.X, 9);
        Assert.Equal(20, first.Y, 9);
        Assert.Equal(735, first.Z, 9);
        Assert.Equal(56.25, next.X, 9);
        Assert.Equal(40, next.Y, 9);
        Assert.Equal(735, next.Z, 9);
    }

    [Fact]
    public void ScalarAxisCompositionPreservesUnmentionedVector3ComponentsAndCurves()
    {
        var original = new AnimationTrack(AnimationProperty.ROTATION_3D,
        [
            new(new(0), AnimationValue.FromVector3(new(10, 20, 30)))
            {
                ComponentCurves = [new(KeyframeInterpolation.EASE_OUT), new(KeyframeInterpolation.POWER) { Exponent = 2.5 }]
            },
            new(new(4), AnimationValue.FromVector3(new(50, 80, 90)))
        ]);
        var layer = new ProjectLayer { End = new(4), Tracks = [original], Transform = new() { RotationX = 10 } };
        var script = EffectScriptRegressionParser.Parse("""
            effect "x-only" version 1
            short-clip compress
            segment turn flex 1
                at 0 rotation-x base
                at 1 rotation-x offset(720)
            end
            """);

        var track = Assert.Single(EffectScriptComposer.Compose(script, layer));

        Assert.Equal(AnimationProperty.ROTATION_3D, track.Property);
        foreach (var time in new MediaTime[] { new(0), new(1), new(2), new(3), new(4) })
        {
            var expected = SceneEvaluator.EvaluateTrack(original, time).Vector3;
            var actual = SceneEvaluator.EvaluateTrack(track, time).Vector3;
            Assert.Equal(10 + (double)time.Numerator / time.Denominator * 180, actual.X, 9);
            Assert.Equal(expected.Y, actual.Y, 9);
            Assert.Equal(expected.Z, actual.Z, 9);
        }
        Assert.Equal(new AnimationCurve(KeyframeInterpolation.EASE_OUT), track.Keyframes[0].GetCurve(1));
    }

    [Fact]
    public void ScalarAxisCompositionRetainsOtherOrderedVector3Components()
    {
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(0), new(4),
            AnimationValue.FromVector3(new(0, 80, 30)), 2.5) { ComponentMask = 2 };
        var original = new AnimationTrack(AnimationProperty.ROTATION_3D, [])
        {
            InitialValue = AnimationValue.FromVector3(new(0, 20, 30)), Transforms = [operation]
        };
        var layer = new ProjectLayer { End = new(4), Tracks = [original] };
        var script = EffectScriptRegressionParser.Parse("""
            effect "x-only" version 1
            short-clip compress
            segment turn flex 1
                at 0 rotation-x 0 power(2)
                at 1 rotation-x 720
            end
            """);

        var track = Assert.Single(EffectScriptComposer.Compose(script, layer));

        Assert.Equal(AnimationProperty.ROTATION_3D, track.Property);
        foreach (var time in new MediaTime[] { new(0), new(1), new(2), new(3), new(4) })
        {
            var expected = SceneEvaluator.EvaluateTrack(original, time).Vector3;
            var actual = SceneEvaluator.EvaluateTrack(track, time).Vector3;
            Assert.Equal(720 * Math.Pow((double)time.Numerator / time.Denominator / 4, 2), actual.X, 9);
            Assert.Equal(expected.Y, actual.Y, 9);
            Assert.Equal(expected.Z, actual.Z, 9);
        }
    }

    [Theory]
    [InlineData(10001)]
    [InlineData(30000)]
    public void ScalarAxisCompositionSupportsTheFullCanonicalTimeUnionBudget(int count)
    {
        var original = new AnimationTrack(AnimationProperty.ROTATION_3D,
            Enumerable.Range(0, count).Select(index => new Keyframe(new(index, count - 1),
                AnimationValue.FromVector3(new(index % 13, index % 17, index % 19)))).ToImmutableArray());
        var layer = new ProjectLayer { End = new(1), Tracks = [original] };
        var script = EffectScriptRegressionParser.Parse("""
            effect "x-only" version 1
            short-clip compress
            segment turn flex 1
                at 0 rotation-x 0
                at 1 rotation-x 720
            end
            """);

        var rotation = Assert.Single(EffectScriptComposer.Compose(script, layer));

        Assert.Equal(AnimationProperty.ROTATION_3D, rotation.Property);
        Assert.Equal(count, rotation.Keyframes.Length);
        foreach (var time in new MediaTime[] { new(0), new(1, 4), new(1, 2), new(3, 4), new(1) })
        {
            var expected = SceneEvaluator.EvaluateTrack(original, time).Vector3;
            var actual = SceneEvaluator.EvaluateTrack(rotation, time).Vector3;
            Assert.Equal(720 * (double)time.Numerator / time.Denominator, actual.X, 9);
            Assert.Equal(expected.Y, actual.Y, 9);
            Assert.Equal(expected.Z, actual.Z, 9);
        }
    }

    [Fact]
    public void PartialAxisCompositionUsesTheCanonicalBudgetForThePreservedScalarProjection()
    {
        const int COUNT = 10001;
        var original = new AnimationTrack(AnimationProperty.ROTATION_3D,
            Enumerable.Range(0, COUNT).Select(index => new Keyframe(new(index, COUNT - 1),
                AnimationValue.FromVector3(new(index % 13, index % 17, index % 19)))).ToImmutableArray());
        var first = new MediaTime(9999, 20000);
        var last = new MediaTime(10001, 20000);
        var script = new EffectScript("partial-x", EffectScriptShortClipPolicy.COMPRESS,
        [
            new("before", null, 1, []),
            new("middle", new(1, 10000), 0,
            [
                new(0, EffectScriptProperty.ROTATION_X, new(EffectScriptValueKind.ABSOLUTE,
                    SceneEvaluator.EvaluateVector3Track(original, first).X)),
                new(0.5m, EffectScriptProperty.ROTATION_X, new(EffectScriptValueKind.ABSOLUTE, 777)),
                new(1, EffectScriptProperty.ROTATION_X, new(EffectScriptValueKind.ABSOLUTE,
                    SceneEvaluator.EvaluateVector3Track(original, last).X))
            ]),
            new("after", null, 1, [])
        ]);
        var layer = new ProjectLayer { End = new(1), Tracks = [original] };

        var rotation = Assert.Single(EffectScriptComposer.Compose(script, layer));

        Assert.Equal(AnimationProperty.ROTATION_3D, rotation.Property);
        Assert.InRange(rotation.Keyframes.Length, COUNT, AnimationPropertyMetadata.GetMaximumTrackEntries(AnimationProperty.ROTATION_3D));
        Assert.Equal(777, SceneEvaluator.EvaluateVector3Track(rotation, new(1, 2)).X, 9);
        foreach (var time in new MediaTime[] { new(0), new(1, 4), new(3, 4), new(1) })
        {
            Assert.Equal(SceneEvaluator.EvaluateVector3Track(original, time), SceneEvaluator.EvaluateVector3Track(rotation, time));
        }
    }

    [Fact]
    public void AxisCompositionRejectsTheFinalCanonicalTimeUnionOverflowWithAnEffectScriptError()
    {
        const int COUNT = 30000;
        var original = new AnimationTrack(AnimationProperty.ROTATION_3D,
            Enumerable.Range(0, COUNT).Select(index => new Keyframe(new(index, COUNT - 1),
                AnimationValue.FromVector3(new(index % 13, index % 17, index % 19)))).ToImmutableArray());
        var first = new MediaTime(9, 20);
        var last = new MediaTime(11, 20);
        var script = new EffectScript("partial-x", EffectScriptShortClipPolicy.COMPRESS,
        [
            new("before", null, 1, []),
            new("middle", new(1, 10), 0,
            [
                new(0, EffectScriptProperty.ROTATION_X, new(EffectScriptValueKind.ABSOLUTE,
                    SceneEvaluator.EvaluateVector3Track(original, first).X)),
                new(0.5m, EffectScriptProperty.ROTATION_X, new(EffectScriptValueKind.ABSOLUTE, 777)),
                new(1, EffectScriptProperty.ROTATION_X, new(EffectScriptValueKind.ABSOLUTE,
                    SceneEvaluator.EvaluateVector3Track(original, last).X))
            ]),
            new("after", null, 1, [])
        ]);
        var layer = new ProjectLayer { End = new(1), Tracks = [original] };

        var error = Assert.Throws<EffectScriptException>(() => EffectScriptComposer.Compose(script, layer));

        Assert.Contains("预算", error.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidDataException>(error.InnerException);
        Assert.Same(original, Assert.Single(layer.Tracks));
        Assert.Equal(COUNT, original.Keyframes.Length);
    }

    [Theory]
    [InlineData(10001)]
    [InlineData(30000)]
    public void UnrelatedCompositionPreservesCanonicalOrderedTracksAtTheFullBudget(int count)
    {
        var rotation = new AnimationTrack(AnimationProperty.ROTATION_3D, [])
        {
            InitialValue = AnimationValue.FromVector3(default),
            Transforms = Enumerable.Range(0, count).Select(index => new AnimationTransformOperation(Guid.NewGuid(),
                new(index, count), new(index + 1, count), AnimationValue.FromVector3(new(index % 13, index % 17, index % 19))))
                .ToImmutableArray()
        };
        var layer = new ProjectLayer { End = new(1), Tracks = [rotation] };

        var composed = EffectScriptComposer.Compose(BuiltinEffectScripts.Get("fade-in").Script, layer);

        Assert.Same(rotation, composed.Single(track => track.Property == AnimationProperty.ROTATION_3D));
        Assert.Equal(2, composed.Length);
    }

    private static string Source(int version)
    {
        var scope = version == 2 ? "scope whole current" : string.Empty;
        var scopeEnd = version == 2 ? "end" : string.Empty;
        return $$"""
            effect "plane-turn" version {{version}}
            short-clip compress
            {{scope}}
            segment turn flex 1
                at 0 rotation-x base
                at 0 rotation-y base
                at 0 perspective-distance base
                at 1 rotation-x offset(720)
                at 1 rotation-y offset(-90)
                at 1 perspective-distance factor(2)
            end
            {{scopeEnd}}
            """;
    }

    private static void AssertScalar(AnimationTrack track, double first, double middle, double last)
    {
        Assert.Equal(AnimationValueKind.SCALAR, track.Keyframes[0].Value.Kind);
        Assert.Equal(first, SceneEvaluator.EvaluateScalarTrack(track, MediaTime.Zero), 9);
        Assert.Equal(middle, SceneEvaluator.EvaluateScalarTrack(track, new(2)), 9);
        Assert.Equal(last, SceneEvaluator.EvaluateScalarTrack(track, new(4)), 9);
    }

    private static void AssertRotation(AnimationTrack track, SceneVector3 first, SceneVector3 middle, SceneVector3 last)
    {
        Assert.Equal(AnimationValueKind.VECTOR3, track.Keyframes[0].Value.Kind);
        Assert.Equal(first, SceneEvaluator.EvaluateTrack(track, MediaTime.Zero).Vector3);
        Assert.Equal(middle, SceneEvaluator.EvaluateTrack(track, new(2)).Vector3);
        Assert.Equal(last, SceneEvaluator.EvaluateTrack(track, new(4)).Vector3);
    }
}
