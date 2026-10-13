using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Projects;

public sealed class ProjectedRotationAnimationTrackMigrationTests
{
    private static readonly int[] masks = [2, 1, 3, 6, 5, 4, 0];

    [Theory]
    [InlineData(10001)]
    [InlineData(30000)]
    public void ProjectedKeyframesUseTheCanonicalBudgetAndKeepEveryComponentCurve(int count)
    {
        var source = Keyframes(count);
        var axes = Components(source);
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.Merge(axes, source.Target, default));

        var merged = Assert.Single(RotationAnimationTrackMigration.MergeProjected(axes, source.Target, default, source));

        Assert.Equal(count, merged.Keyframes.Length);
        Assert.Equal(source.Keyframes.Select(frame => frame.Time), merged.Keyframes.Select(frame => frame.Time));
        Compare(source, merged, count);
        ProjectValidator.Validate(Document(merged), enforceAnimationRange: false);
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(axes[0]), enforceAnimationRange: false));
    }

    [Theory]
    [InlineData(10001)]
    [InlineData(30000)]
    public void FullMaskProjectedOperationsRecombineWithinTheCanonicalBudget(int count)
    {
        var source = Ordered(count);
        var axes = Components(source);

        var merged = Assert.Single(RotationAnimationTrackMigration.MergeProjected(axes, source.Target, default, source));

        Assert.Equal(count, merged.Transforms.Length);
        Assert.Equal(source.Transforms.Select(operation => operation.Id), merged.Transforms.Select(operation => operation.Id));
        Assert.All(merged.Transforms, operation => Assert.Equal(7, operation.ComponentMask));
        Compare(source, merged, count);
        ProjectValidator.Validate(Document(merged), enforceAnimationRange: false);
    }

    [Fact]
    public void ProjectedMixedMasksKeepEachAxisOrderRatherThanTheOrderOfInputTracks()
    {
        var original = Ordered(masks.Length);
        var source = original with
        {
            Transforms = original.Transforms.Select((operation, index) => operation with
            {
                ComponentMask = masks[index]
            }).ToImmutableArray()
        };
        var axes = Components(source);

        var merged = Assert.Single(RotationAnimationTrackMigration.MergeProjected([axes[2], axes[0], axes[1]], source.Target, default, source));

        Assert.Equal(source.Transforms.Length, merged.Transforms.Length);
        Assert.Equal(source.Transforms.Select(operation => operation.Id), merged.Transforms.Select(operation => operation.Id));
        Compare(source, merged, masks.Length);
    }

    [Fact]
    public void OversizedScalarSourcesRequireMatchingCanonicalProvenance()
    {
        var source = Keyframes(10001);
        var axes = Components(source);
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.MergeProjected(axes, source.Target, default));
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.MergeProjected(axes, source.Target, default,
            source with { Target = source.Target with { TextRangeId = Guid.NewGuid() } }));
        var invalidSource = source with { Keyframes = source.Keyframes.Add(source.Keyframes[^1]) };
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.MergeProjected(axes, source.Target, default, invalidSource));
        var wrongKind = source with { Keyframes = [new(new(0), 10)] };
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.MergeProjected(axes, source.Target, default, wrongKind));
    }

    [Fact]
    public void NormalizeProjectedUsesTheCompleteRangeTargetAndRealStaticFallback()
    {
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1) { RotationX = 1, RotationY = 2, Rotation = 3 };
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1) { RotationX = 4, RotationY = 5, Rotation = 6 };
        var line = new SubtitleLine { Text = "AB", AnimationRanges = [first, second] };
        var source = Keyframes(10001) with { Target = new(AnimationProperty.ROTATION_3D, TextRangeId: first.Id) };
        var axis = Component(source, 0);
        var other = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.ROTATION_Y, TextRangeId: second.Id), [new(new(0), 200)]);
        var layer = new ProjectLayer { SubtitleId = line.Id, End = new(40), Tracks = [axis, other] };

        var tracks = RotationAnimationTrackMigration.NormalizeProjected(layer, line, [source]);

        Assert.Equal(source.Target, tracks[0].Target);
        Assert.Equal(new SceneVector3(source.Keyframes[0].Value.Vector3.X, 2, 3), SceneEvaluator.EvaluateVector3Track(tracks[0], new(0)));
        Assert.Equal(new SceneVector3(4, 200, 6), SceneEvaluator.EvaluateVector3Track(tracks[1], new(0)));
        var otherOversized = axis with { Target = axis.Target with { TextRangeId = second.Id } };
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.NormalizeProjected(layer with { Tracks = [axis, otherOversized] }, line, [source]));
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.NormalizeProjected(layer, line, [source, source]));
    }

    [Fact]
    public void ProjectedMergeStillEnforcesTheFinalCanonicalTimeUnionBudget()
    {
        var source = Keyframes(30000);
        var x = Component(source, 0);
        var y = Component(source, 1) with
        {
            Keyframes = Component(source, 1).Keyframes.Select(frame => frame with { Time = frame.Time + new MediaTime(1, 2000) }).ToImmutableArray()
        };

        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.MergeProjected([x, y], source.Target, default, source));
    }

    [Fact]
    public void ProjectedOperationUnionAboveBudgetIsRejectedEvenWithSourceEvidence()
    {
        var source = Ordered(30000);
        var x = Component(source, 0);
        var y = Component(source, 1) with
        {
            Transforms = Component(source, 1).Transforms.Select(operation => operation with
            {
                Curve = new(KeyframeInterpolation.HOLD)
            }).ToImmutableArray()
        };

        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.MergeProjected([x, y], source.Target, default, source));
    }

    [Fact]
    public void ProvenanceCannotSkipScalarShapeCurveOrCanonicalSourceBudgetValidation()
    {
        var source = Ordered(1);
        var x = Component(source, 0);
        var wrongMask = x with { Transforms = [x.Transforms[0] with { ComponentMask = 2 }] };
        var invalidCurve = x with
        {
            Transforms = [x.Transforms[0] with { Curve = new(KeyframeInterpolation.POWER) { Exponent = double.NaN } }]
        };

        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.MergeProjected([wrongMask], source.Target, default, source));
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.MergeProjected([invalidCurve], source.Target, default, source));
        Assert.Throws<InvalidDataException>(() => RotationAnimationTrackMigration.MergeProjected([x], source.Target, default, Ordered(30001)));
    }

    [Fact]
    public void SameIdsWithoutMatchingSourceOperationsKeepTheLegacyCollisionSemantics()
    {
        var id = Guid.NewGuid();
        var x = ScalarOrdered(AnimationProperty.ROTATION_X, 1, [new(id, new(0), new(4), 100)]);
        var y = ScalarOrdered(AnimationProperty.ROTATION_Y, 2, [new(id, new(0), new(4), 200)]);
        var source = Ordered(1);

        var merged = Assert.Single(RotationAnimationTrackMigration.MergeProjected([x, y], source.Target, new(1, 2, 3), source));
        var withoutSource = Assert.Single(RotationAnimationTrackMigration.MergeProjected([x, y], source.Target, new(1, 2, 3)));

        Assert.Equal(2, merged.Transforms.Length);
        Assert.Equal(merged.Transforms.AsEnumerable(), withoutSource.Transforms.AsEnumerable());
        CompareAxes(merged, [x, y, null], new(1, 2, 3));
    }

    [Fact]
    public void ChangedOperationTimingAndCurvesPreventCoalescingDespiteMatchingIds()
    {
        var source = Ordered(1);
        var axes = Components(source);
        var y = axes[1] with
        {
            Transforms = [axes[1].Transforms[0] with { Start = new(-1), Curve = new(KeyframeInterpolation.HOLD) { Reverse = true } }]
        };

        var merged = Assert.Single(RotationAnimationTrackMigration.MergeProjected([axes[0], y, axes[2]], source.Target, default, source));

        Assert.Equal(2, merged.Transforms.Length);
        Assert.Equal(2, merged.Transforms.Select(operation => operation.Id).Distinct().Count());
        CompareAxes(merged, [axes[0], y, axes[2]], default);
    }

    [Fact]
    public void ConflictingAxisOrderKeepsSeparateExactOperationsInsteadOfReorderingAnAxis()
    {
        var source = Ordered(2);
        var axes = Components(source);
        var y = axes[1] with { Transforms = axes[1].Transforms.Reverse().ToImmutableArray() };

        var merged = Assert.Single(RotationAnimationTrackMigration.MergeProjected([axes[0], y, axes[2]], source.Target, default, source));

        Assert.Equal(6, merged.Transforms.Length);
        Assert.Equal(6, merged.Transforms.Select(operation => operation.Id).Distinct().Count());
        CompareAxes(merged, [axes[0], y, axes[2]], default);
        ProjectValidator.Validate(Document(merged), enforceAnimationRange: false);
    }

    [Fact]
    public void MixedProjectedOrderedAndNewKeyframesKeepIndependentExactCurves()
    {
        var source = Ordered(3);
        var axes = Components(source);
        var x = new AnimationTrack(AnimationProperty.ROTATION_X,
        [
            new(new(-1), 10, KeyframeInterpolation.POWER) { CurveStart = 0.1, CurveEnd = 0.9, Exponent = 2.5, Reverse = true },
            new(new(2), 110, KeyframeInterpolation.HOLD) { Reverse = true },
            new(new(7), -30)
        ]);

        var merged = Assert.Single(RotationAnimationTrackMigration.MergeProjected([axes[2], x, axes[1]], source.Target, default, source));

        Assert.Equal(source.Transforms.Length + 2, merged.Transforms.Length);
        Assert.Equal(merged.Transforms.Length, merged.Transforms.Select(operation => operation.Id).Distinct().Count());
        CompareAxes(merged, [x, axes[1], axes[2]], default);
    }

    private static AnimationTrack Keyframes(int count)
    {
        return new(AnimationProperty.ROTATION_3D, Enumerable.Range(0, count).Select(index =>
            new Keyframe(new(index, 1000), new SceneVector3(index % 17, index % 31, -index), KeyframeInterpolation.POWER)
            {
                CurveStart = 0.1,
                CurveEnd = 0.9,
                Exponent = 2.5,
                Reverse = true,
                ComponentCurves =
                [
                    new(KeyframeInterpolation.HOLD) { Reverse = index % 2 == 0 },
                    new(KeyframeInterpolation.EASE_IN_OUT, 0.2, 0.8)
                ]
            }).ToImmutableArray());
    }

    private static AnimationTrack Ordered(int count)
    {
        return new(AnimationProperty.ROTATION_3D, [])
        {
            InitialValue = new SceneVector3(1, 2, 3),
            Transforms = Enumerable.Range(0, count).Select(index =>
                new AnimationTransformOperation(Guid.NewGuid(), new(index % 5 - 1), new(index % 3 + 7),
                    new SceneVector3(index % 17, index % 31, -index), index % 4 * 0.7)
                {
                    Curve = index % 2 == 0 ? new(KeyframeInterpolation.POWER, 0.1, 0.9) { Exponent = 0.7, Reverse = true } : null
                }).ToImmutableArray()
        };
    }

    private static AnimationTrack ScalarOrdered(AnimationProperty property, double initial,
        ImmutableArray<AnimationTransformOperation> transforms) => new(property, []) { InitialValue = initial, Transforms = transforms };

    private static ImmutableArray<AnimationTrack> Components(AnimationTrack source)
    {
        return [Component(source, 0), Component(source, 1), Component(source, 2)];
    }

    private static AnimationTrack Component(AnimationTrack source, int component)
    {
        var target = source.Target with { Property = AnimationPropertyMetadata.GetLegacyComponents(AnimationProperty.ROTATION_3D)[component] };
        if (source.IsOrdered)
        {
            return new(target, [])
            {
                InitialValue = source.InitialValue!.Value.GetComponent(component),
                Transforms = source.Transforms.Where(operation => operation.ComponentMask == 0 || (operation.ComponentMask & (1 << component)) != 0)
                    .Select(operation => operation with { Value = operation.Value.GetComponent(component), ComponentMask = 0 }).ToImmutableArray()
            };
        }

        return new(target, source.Keyframes.Select(frame =>
        {
            var curve = frame.GetCurve(component);
            return frame with
            {
                Value = frame.Value.GetComponent(component),
                Interpolation = curve.Interpolation,
                CurveStart = curve.CurveStart,
                CurveEnd = curve.CurveEnd,
                Exponent = curve.Exponent,
                Reverse = curve.Reverse,
                ComponentCurves = []
            };
        }).ToImmutableArray());
    }

    private static ProjectDocument Document(AnimationTrack track) => new()
    {
        Layers = [new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(40), Tracks = [track] }]
    };

    private static void Compare(AnimationTrack expected, AnimationTrack actual, int count)
    {
        var times = new MediaTime[] { new(-1), new(0), new(1, 2000), new MediaTime(count - 2, 1000) + new MediaTime(1, 2000), new(count - 1, 1000), new(2), new(5), new(8), new(10), new(40) };
        foreach (var time in times)
        {
            var value = SceneEvaluator.EvaluateTrack(expected, time);
            var result = SceneEvaluator.EvaluateTrack(actual, time);
            for (var component = 0; component < value.ComponentCount; component++)
            {
                Assert.Equal(value.GetComponent(component), result.GetComponent(component), 9);
            }
        }
    }

    private static void CompareAxes(AnimationTrack actual, AnimationTrack?[] axes, SceneVector3 fallback)
    {
        for (var index = -100; index <= 1000; index++)
        {
            var time = new MediaTime(index, 100);
            var result = SceneEvaluator.EvaluateTrack(actual, time);
            for (var component = 0; component < result.ComponentCount; component++)
            {
                var expected = axes[component] is { } axis ? SceneEvaluator.EvaluateScalarTrack(axis, time) :
                    AnimationValue.FromVector3(fallback).GetComponent(component);
                Assert.Equal(expected, result.GetComponent(component), 9);
            }
        }
    }
}
