using System.Collections.Immutable;
using System.Text.Json;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Projects;

public sealed class RotationAnimationTrackMigrationTests
{
    private static readonly int[] xyMasks = [1, 1, 2, 2];
    private static readonly int[] xzMasks = [1, 1, 4, 4];

    [Fact]
    public void ScalarKeyframesMergeTheirTimeUnionAndKeepAllCurvePhases()
    {
        var x = new AnimationTrack(AnimationProperty.ROTATION_X,
        [
            new(new(-2), 10, KeyframeInterpolation.POWER) { CurveStart = 0.1, CurveEnd = 0.9, Exponent = 2.5, Reverse = true },
            new(new(8), 210)
        ]);
        var y = new AnimationTrack(AnimationProperty.ROTATION_Y,
        [
            new(new(0), 20, KeyframeInterpolation.HOLD) { Reverse = true },
            new(new(3), 100, KeyframeInterpolation.EASE_OUT) { CurveStart = 0.2, CurveEnd = 0.8 },
            new(new(8), 220)
        ]);
        var z = new AnimationTrack(AnimationProperty.ROTATION,
        [
            new(new(1), 30, KeyframeInterpolation.EASE_IN),
            new(new(5), 330, KeyframeInterpolation.POWER) { Exponent = 0.7, Reverse = true },
            new(new(7), 750)
        ]);
        var merged = Assert.Single(RotationAnimationTrackMigration.Merge([z, x, y], Target(), new(1, 2, 3)));
        Assert.False(merged.IsOrdered);
        Assert.Equal(new MediaTime[] { new(-2), new(0), new(1), new(3), new(5), new(7), new(8) }, merged.Keyframes.Select(frame => frame.Time));
        Assert.Equal(2, merged.Keyframes[0].ComponentCurves.Length);
        Assert.True(merged.Keyframes[0].Reverse);
        CompareAxes(merged, x, y, z, new(1, 2, 3));
        ProjectValidator.Validate(Document(merged), enforceAnimationRange: false);
    }

    [Theory]
    [InlineData(KeyframeInterpolation.HOLD, false)]
    [InlineData(KeyframeInterpolation.HOLD, true)]
    [InlineData(KeyframeInterpolation.LINEAR, false)]
    [InlineData(KeyframeInterpolation.LINEAR, true)]
    [InlineData(KeyframeInterpolation.EASE_IN, false)]
    [InlineData(KeyframeInterpolation.EASE_IN, true)]
    [InlineData(KeyframeInterpolation.EASE_OUT, false)]
    [InlineData(KeyframeInterpolation.EASE_OUT, true)]
    [InlineData(KeyframeInterpolation.EASE_IN_OUT, false)]
    [InlineData(KeyframeInterpolation.EASE_IN_OUT, true)]
    [InlineData(KeyframeInterpolation.POWER, false)]
    [InlineData(KeyframeInterpolation.POWER, true)]
    public void MixedOrderedAndKeyframesPreserveEachOriginalCurveAtAndAroundEveryEndpoint(
        KeyframeInterpolation interpolation, bool reverse)
    {
        var x = Ordered(AnimationProperty.ROTATION_X, 10,
        [
            new(Guid.NewGuid(), new(0), new(6), 90, 0),
            new(Guid.NewGuid(), new(2), new(8), -40, 2.5)
        ]);
        var y = new AnimationTrack(AnimationProperty.ROTATION_Y,
        [
            new(new(-2), 20, interpolation) { CurveStart = 0.1, CurveEnd = 0.9, Exponent = 0.7, Reverse = reverse },
            new(new(3), 220, interpolation) { CurveStart = 0.2, CurveEnd = 0.8, Exponent = 2.5, Reverse = !reverse },
            new(new(8), -100)
        ]);
        var z = new AnimationTrack(AnimationProperty.ROTATION, [new(new(5), 750)]);
        var merged = Assert.Single(RotationAnimationTrackMigration.Merge([y, z, x], Target(), new(1, 2, 3)));
        Assert.True(merged.IsOrdered);
        Assert.Empty(merged.Keyframes);
        Assert.Equal(new SceneVector3(10, 20, 750), merged.InitialValue!.Value.Vector3);
        Assert.Equal(4, merged.Transforms.Length);
        Assert.Equal(x.Transforms.Select(operation => operation.Id), merged.Transforms.Take(2).Select(operation => operation.Id));
        Assert.Equal(y.Keyframes[0].GetCurve(0), merged.Transforms[2].Curve);
        Assert.Equal(y.Keyframes[1].GetCurve(0), merged.Transforms[3].Curve);
        Assert.Equal(xyMasks, merged.Transforms.Select(operation => operation.ComponentMask));
        CompareAxes(merged, x, y, z, new(1, 2, 3));
        ProjectValidator.Validate(Document(merged));
        var restored = Assert.IsType<AnimationTrack>(JsonSerializer.Deserialize<AnimationTrack>(JsonSerializer.Serialize(merged)));
        Assert.Equal(merged.InitialValue, restored.InitialValue);
        Assert.Equal(merged.Transforms.Select(operation => operation.Value), restored.Transforms.Select(operation => operation.Value));
        Assert.Equal(merged.Transforms.Select(operation => operation.Curve), restored.Transforms.Select(operation => operation.Curve));
        Assert.Equal(merged.Transforms.Select(operation => operation.ComponentMask), restored.Transforms.Select(operation => operation.ComponentMask));
    }

    [Fact]
    public void OrderedAxisMasksAndSourceOrderStayExactIncludingInstantaneousOperations()
    {
        var id = Guid.NewGuid();
        var x = Ordered(AnimationProperty.ROTATION_X, 10,
        [
            new(id, new(2), new(8), 90, 2),
            new(Guid.NewGuid(), new(0), new(6), -40, 0.7) { ComponentMask = 1 }
        ]);
        var z = Ordered(AnimationProperty.ROTATION, 30,
        [
            new(id, new(-1), new(-1), 750),
            new(Guid.NewGuid(), new(3), new(7), -20, 10)
            {
                Curve = new(KeyframeInterpolation.EASE_IN_OUT, 0.2, 0.8) { Reverse = true }
            }
        ]);
        var merged = Assert.Single(RotationAnimationTrackMigration.Merge([z, x], Target(), new(1, 222, 3)));
        var again = Assert.Single(RotationAnimationTrackMigration.Merge([z, x], Target(), new(1, 222, 3)));
        Assert.Equal(4, merged.Transforms.Select(operation => operation.Id).Distinct().Count());
        Assert.Equal(merged.Transforms.Select(operation => operation.Id), again.Transforms.Select(operation => operation.Id));
        Assert.Equal(xzMasks, merged.Transforms.Select(operation => operation.ComponentMask));
        Assert.Equal(x.Transforms[0].Id, merged.Transforms[0].Id);
        Assert.NotEqual(z.Transforms[0].Id, merged.Transforms[2].Id);
        Assert.Equal(z.Transforms[1].Curve, merged.Transforms[3].Curve);
        CompareAxes(merged, x, null, z, new(1, 222, 3));
        ProjectValidator.Validate(Document(merged));
    }

    [Fact]
    public void MergeUsesTheCompleteTargetAndKeepsOtherScopesInTheirOriginalOrder()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var target = Target(first);
        var unrelated = new AnimationTrack(AnimationProperty.OPACITY, [new(new(0), 0.8)]);
        var whole = new AnimationTrack(AnimationProperty.ROTATION, [new(new(0), 300)]);
        var other = new AnimationTrack(Target(second) with { Property = AnimationProperty.ROTATION_Y }, [new(new(0), 200)]);
        var active = new AnimationTrack(target with { Property = AnimationProperty.ROTATION_X, State = SubtitleAnimationState.ACTIVE }, [new(new(0), 100)]);
        var axis = new AnimationTrack(target with { Property = AnimationProperty.ROTATION_X }, [new(new(0), 50)]);
        var result = RotationAnimationTrackMigration.Merge([unrelated, axis, other, whole, active], target, new(1, 2, 3));
        Assert.Equal(new[] { unrelated.Target, target, other.Target, whole.Target, active.Target }, result.Select(track => track.Target));
        Assert.Same(unrelated, result[0]);
        Assert.Same(other, result[2]);
        Assert.Same(whole, result[3]);
        Assert.Same(active, result[4]);
        Assert.Equal(new SceneVector3(50, 2, 3), SceneEvaluator.EvaluateVector3Track(result[1], new(0)));
    }

    [Fact]
    public void NormalizeReadsEachRangeFallbackAndIsIdempotentAfterCanonicalization()
    {
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1) { RotationX = 1, RotationY = 2, Rotation = 3 };
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1) { RotationX = 4, RotationY = 5, Rotation = 6 };
        var line = new SubtitleLine { Text = "AB", AnimationRanges = [first, second] };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id,
            End = line.End,
            Transform = new() { RotationX = 10, RotationY = 20, Rotation = 30 },
            Tracks =
            [
                new(AnimationProperty.ROTATION, [new(new(0), 300)]),
                new(Target(first.Id) with { Property = AnimationProperty.ROTATION_X }, [new(new(0), 100)]),
                new(Target(second.Id) with { Property = AnimationProperty.ROTATION_Y }, [new(new(0), 200)])
            ]
        };
        var tracks = RotationAnimationTrackMigration.Normalize(layer, line);
        Assert.Equal(3, tracks.Length);
        Assert.All(tracks, track => Assert.Equal(AnimationProperty.ROTATION_3D, track.Property));
        Assert.Equal(new SceneVector3(10, 20, 300), SceneEvaluator.EvaluateVector3Track(tracks[0], new(0)));
        Assert.Equal(new SceneVector3(100, 2, 3), SceneEvaluator.EvaluateVector3Track(tracks[1], new(0)));
        Assert.Equal(new SceneVector3(4, 200, 6), SceneEvaluator.EvaluateVector3Track(tracks[2], new(0)));
        Assert.Equal(tracks, RotationAnimationTrackMigration.Normalize(layer with { Tracks = tracks }, line));
        ProjectValidator.Validate(new() { Layers = [layer with { Tracks = tracks }], Subtitles = [line] });
    }

    [Fact]
    public void CanonicalAndLegacyAxesCannotCoexistOnlyWithinTheSameCompleteTarget()
    {
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        var line = new SubtitleLine { Text = "AB", AnimationRanges = [first, second] };
        var canonical = new AnimationTrack(Target(first.Id), [new(new(0), new SceneVector3(1, 2, 3))]);
        var local = new AnimationTrack(Target(second.Id) with { Property = AnimationProperty.ROTATION }, [new(new(0), 30)]);
        var whole = new AnimationTrack(AnimationProperty.ROTATION, [new(new(0), 60)]);
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Tracks = [canonical, local, whole] };
        ProjectValidator.Validate(new() { Layers = [layer], Subtitles = [line] });
        foreach (var property in new[] { AnimationProperty.ROTATION_X, AnimationProperty.ROTATION_Y, AnimationProperty.ROTATION })
        {
            var conflict = local with { Target = canonical.Target with { Property = property } };
            Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
            {
                Layers = [layer with { Tracks = [canonical, conflict] }], Subtitles = [line]
            }));
            Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.Merge([canonical, conflict], canonical.Target, new(0, 0, 0)));
        }
    }

    [Fact]
    public void InvalidLegacyTrackDataIsRejectedBeforeItCanBeMerged()
    {
        var valid = new AnimationTrack(AnimationProperty.ROTATION_X, [new(new(0), 10), new(new(8), 90)]);
        var invalid = new AnimationTrack[]
        {
            valid with { Keyframes = [] },
            valid with { Keyframes = default },
            valid with { Keyframes = [new(new(1), 10), new(new(0), 90)] },
            valid with { Keyframes = [new(new(0), double.NaN)] },
            valid with { Keyframes = [new(new(0), new SceneVector3(1, 2, 3))] },
            valid with { Keyframes = [new(new(0), 10) { ComponentCurves = [new(KeyframeInterpolation.HOLD)] }] },
            valid with { Keyframes = [new(new(0), 10) { CurveStart = 0.8, CurveEnd = 0.2 }] },
            valid with { ColorSpace = AnimationColorSpace.SRGB },
            Ordered(AnimationProperty.ROTATION_X, 10, [new(Guid.NewGuid(), new(2), new(1), 90)]),
            Ordered(AnimationProperty.ROTATION_X, 10, [new(Guid.NewGuid(), new(0), new(1), 90) { ComponentMask = 2 }]),
            Ordered(AnimationProperty.ROTATION_X, 10, [new(Guid.NewGuid(), new(0), new(1), 90, -1)]),
            Ordered(AnimationProperty.ROTATION_X, 10, [new(Guid.NewGuid(), new(0), new(1), 90) { Curve = new(KeyframeInterpolation.POWER) { Exponent = double.NaN } }])
        };
        foreach (var track in invalid)
        {
            Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.Merge([track], Target(), new(1, 2, 3)));
        }
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.Merge([valid, valid], Target(), new(1, 2, 3)));
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.Merge([valid], new(AnimationProperty.ROTATION), new(1, 2, 3)));
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.Merge([valid], Target(), new(1, 2, double.PositiveInfinity)));
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.Merge(default, Target(), new(1, 2, 3)));
        var overBudget = valid with
        {
            Keyframes = Enumerable.Range(0, 10001).Select(index => new Keyframe(new(index), index)).ToImmutableArray()
        };
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.Merge([overBudget], Target(), new(1, 2, 3)));
    }

    [Fact]
    public void ConvertedMixedOperationsKeepTheirExactContentClockWhenClippedAndStretched()
    {
        var x = Ordered(AnimationProperty.ROTATION_X, 10, [new(Guid.NewGuid(), new(-2), new(8), 110, 2.5)]);
        var y = new AnimationTrack(AnimationProperty.ROTATION_Y,
        [
            new(new(-2), 20, KeyframeInterpolation.POWER) { Exponent = 0.7, CurveStart = 0.1, CurveEnd = 0.9, Reverse = true },
            new(new(3), 100, KeyframeInterpolation.HOLD) { Reverse = true },
            new(new(8), 200)
        ]);
        var merged = Assert.Single(RotationAnimationTrackMigration.Merge([x, y], Target(), new(1, 2, 720)));
        var layer = Document(merged).Layers[0];
        var cropped = LayerAnimationTiming.Retime(layer, new(2), new(6), TimelineEditMode.CROP);
        var stretched = LayerAnimationTiming.Retime(layer, new(0), new(16), TimelineEditMode.STRETCH);
        ProjectValidator.Validate(new() { Layers = [cropped] });
        ProjectValidator.Validate(new() { Layers = [stretched] });
        for (var index = 0; index <= 80; index++)
        {
            var time = new MediaTime(40 + index, 20);
            Equal(SceneEvaluator.EvaluateVector3Track(merged, time), SceneEvaluator.EvaluateVector3Track(cropped.Tracks[0], time));
            Equal(SceneEvaluator.EvaluateVector3Track(merged, time), SceneEvaluator.EvaluateVector3Track(stretched.Tracks[0], time * 2));
        }
    }

    [Fact]
    public void OptionalOperationCurveKeepsOldAccelerationWhenAbsentAndRoundTripsWhenPresent()
    {
        var old = Ordered(AnimationProperty.ROTATION, 10, [new(Guid.NewGuid(), new(0), new(4), 110, 2)]);
        var curved = old with
        {
            Transforms = [old.Transforms[0] with { Curve = new(KeyframeInterpolation.EASE_OUT, 0.1, 0.9) { Reverse = true } }]
        };
        var oldEncoded = JsonSerializer.Serialize(old);
        Assert.DoesNotContain("\"Curve\"", oldEncoded, StringComparison.Ordinal);
        var restoredOld = Assert.IsType<AnimationTrack>(JsonSerializer.Deserialize<AnimationTrack>(oldEncoded));
        Assert.Null(restoredOld.Transforms[0].Curve);
        Assert.Equal(old.Transforms[0].Acceleration, restoredOld.Transforms[0].Acceleration);
        Assert.Equal(old.Transforms[0].Value, restoredOld.Transforms[0].Value);
        Assert.Equal(16.25, SceneEvaluator.EvaluateScalarTrack(old, new(1)), 10);
        var restoredCurve = Assert.IsType<AnimationTrack>(JsonSerializer.Deserialize<AnimationTrack>(JsonSerializer.Serialize(curved)));
        Assert.Equal(curved.Transforms[0].Curve, restoredCurve.Transforms[0].Curve);
        var keyframe = new AnimationTrack(AnimationProperty.ROTATION,
        [
            new(new(0), 10, KeyframeInterpolation.EASE_OUT) { CurveStart = 0.1, CurveEnd = 0.9, Reverse = true },
            new(new(4), 110)
        ]);
        for (var index = -1; index <= 41; index++)
        {
            var time = new MediaTime(index, 10);
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(keyframe, time), SceneEvaluator.EvaluateScalarTrack(curved, time), 10);
        }
    }

    private static AnimationTrackTarget Target(Guid? range = null) => new(AnimationProperty.ROTATION_3D, TextRangeId: range);

    private static AnimationTrack Ordered(AnimationProperty property, double initial,
        ImmutableArray<AnimationTransformOperation> transforms) => new(property, []) { InitialValue = initial, Transforms = transforms };

    private static ProjectDocument Document(AnimationTrack track) => new()
    {
        Layers = [new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(8), Tracks = [track] }]
    };

    private static void CompareAxes(AnimationTrack merged, AnimationTrack? x, AnimationTrack? y, AnimationTrack? z, SceneVector3 fallback)
    {
        var axes = new[] { x, y, z };
        var times = Enumerable.Range(-60, 281).Select(index => new MediaTime(index, 20))
            .Concat(axes.Where(axis => axis is not null).SelectMany(axis => axis!.IsOrdered
                ? axis.Transforms.SelectMany(operation => new[] { operation.Start, operation.End })
                : axis.Keyframes.Select(frame => frame.Time)).SelectMany(time => new[] { time - new MediaTime(1, 1000), time, time + new MediaTime(1, 1000) }))
            .Distinct().Order();
        foreach (var time in times)
        {
            var actual = SceneEvaluator.EvaluateTrack(merged, time);
            for (var component = 0; component < axes.Length; component++)
            {
                var expected = axes[component] is { } source ? SceneEvaluator.EvaluateScalarTrack(source, time) :
                    AnimationValue.FromVector3(fallback).GetComponent(component);
                Assert.Equal(expected, actual.GetComponent(component), 9);
            }
        }
    }

    private static void Equal(SceneVector3 expected, SceneVector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Z, actual.Z, 9);
    }
}
